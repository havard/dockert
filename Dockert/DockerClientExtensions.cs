using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using ICSharpCode.SharpZipLib.Tar;

namespace Dockert
{
    public static class DockerClientExtensions
    {
        public static async Task<string> CreateContainer(this IDockerClient dockerClient, ContainerOptions containerOptions, CancellationToken cancellationToken = default)
        {
            var response = await dockerClient.Images.ListImagesAsync(new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>> { [ "reference" ] = new Dictionary<string, bool> { [containerOptions.ImageName] = true } }
            });

            if (!response.Any())
            {
                await dockerClient.Images.CreateImageAsync(new ImagesCreateParameters
                {
                    FromImage = containerOptions.ImageName
                },
                new AuthConfig(),
                new Progress<JSONMessage>(),
                cancellationToken);
            }

            var createContainerParameters = new CreateContainerParameters
            {
                Image = containerOptions.ImageName,
                Env = containerOptions.EnvironmentVariables,
                ExposedPorts = new Dictionary<string, EmptyStruct>(containerOptions.PortBindings.Select(portDeclaration =>
                {
                    var portDeclarationParts = portDeclaration.Split(":");
                    return new KeyValuePair<string, EmptyStruct>(portDeclarationParts[0], new EmptyStruct());
                })),
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>(containerOptions.PortBindings.Select(portDeclaration =>
                    {
                        var portDeclarationParts = portDeclaration.Split(":");
                        return new KeyValuePair<string, IList<PortBinding>>(portDeclarationParts[0], new[] { new PortBinding { HostPort = portDeclarationParts[1] } });
                    }))
                },
                Entrypoint = containerOptions.EntryPoint != null ? ParseCommand(containerOptions.EntryPoint).ToList() : null,
                Labels = new Dictionary<string, string> { ["dockert"] = "container" },
            };

            var containerResponse = await dockerClient.Containers.CreateContainerAsync(createContainerParameters, cancellationToken);

            return containerResponse.ID;
        }

        public static async Task StartContainer(this IDockerClient dockerClient, string containerId, CancellationToken cancellationToken = default)
        {
            await dockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
        }

        public static async Task StopAndRemoveContainer(this IDockerClient dockerClient, string containerId, CancellationToken cancellationToken = default)
        {
            await dockerClient.Containers.StopContainerAsync(containerId, new ContainerStopParameters
            {
                WaitBeforeKillSeconds = 10
            }, cancellationToken);

            await dockerClient.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters
            {
                Force = true,
                RemoveVolumes = true
            }, cancellationToken);
        }

        public static async Task<IEnumerable<string>> ListAllContainers(this IDockerClient dockerClient)
        {
            var response = await dockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true
            });

            return response.Select(list => list.ID);
        }

        public static async Task PruneContainers(this IDockerClient dockerClient, bool includeRunning = false, CancellationToken cancellationToken = default)
        {
            var containers = await dockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true
            },
            cancellationToken);

            if (!containers.Any())
            {
                return;
            }

            foreach (var container in containers.Where(c => c.Labels.ContainsKey("dockert") && c.Labels["dockert"] == "container" && (includeRunning == true || (c.Status != "Running" && !c.Status.StartsWith("Up ")))))
            {
                await dockerClient.StopAndRemoveContainer(container.ID, cancellationToken);
            }
        }

        public static async Task CopyFiles(this IDockerClient dockerClient, string containerId, (string SourceFile, string? TargetFile, int Permissions)[] files, CancellationToken cancellationToken = default)
        {
            using var memoryStream = new MemoryStream();
            await CreateTarArchive(memoryStream, files);

            memoryStream.Seek(0, SeekOrigin.Begin);
            await dockerClient.Containers.ExtractArchiveToContainerAsync(containerId, new ContainerPathStatParameters
            {
                Path = "/",
            }, memoryStream, cancellationToken);
        }

        public static async Task GetContainerOutput(this IDockerClient dockerClient, string containerId, Stream standardOutput, Stream standardError, DateTimeOffset? since = default, uint? tail = default, bool follow = false, CancellationToken cancellationToken = default)
        {
            if (standardOutput == Stream.Null && standardError == Stream.Null)
            {
                return;
            }

            var logStream = await dockerClient.Containers.GetContainerLogsAsync(containerId, false, new ContainerLogsParameters
            {
                ShowStdout = standardOutput != Stream.Null,
                ShowStderr = standardError != Stream.Null,
                Follow = follow,
                Since = since.HasValue ? since.Value.ToUnixTimeSeconds().ToString() : null,
                Timestamps = false,
                Tail = tail.HasValue ? tail.ToString() : null
            }, cancellationToken);

            await logStream.CopyOutputToAsync(Stream.Null, standardOutput, standardError, cancellationToken);
        }

        public static Task<long> RunCommand(this IDockerClient dockerClient, string containerId, string command, CancellationToken cancellationToken = default)
        {
            return dockerClient.RunCommand(containerId, ParseCommand(command).ToArray(), cancellationToken);
        }

        public static Task<long> RunCommand(this IDockerClient dockerClient, string containerId, string[] command, CancellationToken cancellationToken = default)
        {
            return dockerClient.RunCommand(containerId, command, Stream.Null, Stream.Null, cancellationToken);
        }

        public static Task<long> RunCommand(this IDockerClient dockerClient, string containerId, string command, Stream standardOutput, Stream standardError, CancellationToken cancellationToken = default)
        {
            return dockerClient.RunCommand(containerId, ParseCommand(command).ToArray(), standardOutput, standardError, cancellationToken);
        }

        public static async Task<long> RunCommand(this IDockerClient dockerClient, string containerId, string[] command, Stream standardOutput, Stream standardError, CancellationToken cancellationToken = default)
        {
            var execCreateResponse = await dockerClient.Exec.ExecCreateContainerAsync(containerId, new ContainerExecCreateParameters
            {
                AttachStderr = standardError != Stream.Null,
                AttachStdout = standardOutput != Stream.Null,
                Cmd = command,
            }, cancellationToken);

            var attachResponse = await dockerClient.Exec.StartAndAttachContainerExecAsync(execCreateResponse.ID, false, cancellationToken);

            Task streamOutputTask = Task.CompletedTask;
            if (standardOutput != Stream.Null || standardError != Stream.Null)
            {
                streamOutputTask = attachResponse.CopyOutputToAsync(Stream.Null, standardOutput, standardError, cancellationToken);
            }

            ContainerExecInspectResponse execInspectResponse;
            do
            {
                execInspectResponse = await dockerClient.Exec.InspectContainerExecAsync(execCreateResponse.ID, cancellationToken);
            }
            while (execInspectResponse.Running);

            await streamOutputTask;
            
            return execInspectResponse.ExitCode;
        }

        private static IEnumerable<string> ParseCommand(string command)
        {
            var builder = new StringBuilder();
            bool quoted = false, escaped = false;
            foreach (char c in command)
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    if (quoted && !escaped)
                    {
                        quoted = false;
                    }
                    else
                    {
                        quoted = true;
                    }
                }
                else if (c == ' ' && !quoted)
                {
                    yield return builder.ToString();
                    builder.Clear();
                    continue;
                }

                if (escaped && c != '\\')
                {
                    escaped = false;
                }

                if (c != '"' || escaped)
                {
                    builder.Append(c);
                }
            }

            if (quoted)
            {
                throw new ArgumentException("Unmatched quotes in string literal");
            }

            yield return builder.ToString();
        }

        private static async Task CreateTarArchive(Stream stream, params (string SourceFile, string? TargetFile, int Permissions)[] files)
        {
            using var tarStream = new TarOutputStream(stream, Encoding.Default)
            {
                IsStreamOwner = false
            };

            foreach (var (SourceFile, TargetFile, Permissions) in files)
            {
                var fileSize = new FileInfo(SourceFile).Length;

                tarStream.PutNextEntry(new TarEntry(
                    new TarHeader
                    {
                        Name = TargetFile ?? SourceFile,
                        Mode = Permissions,
                        Size = fileSize
                    }));

                using var fileStream = File.OpenRead(SourceFile);
                await fileStream.CopyToAsync(tarStream);

                tarStream.CloseEntry();
            }
        }
    }
}
