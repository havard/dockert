using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;

namespace Dockert
{
    public class AsyncDisposableContainer : IAsyncDisposable, IDisposable
    {
        private readonly IDockerClient dockerClient;
        private readonly string containerId;

        protected AsyncDisposableContainer(IDockerClient dockerClient, string containerId)
        {
            this.dockerClient = dockerClient;
            this.containerId = containerId;
        }

        public string Id => containerId;

        public static Task<AsyncDisposableContainer> FromImage(string imageName, CancellationToken cancellationToken = default)
        {
            return Create(new ContainerOptions { ImageName = imageName }, (dockerClient, containerId) => new AsyncDisposableContainer(dockerClient, containerId), cancellationToken);
        }
        public static Task<AsyncDisposableContainer> FromImage(ContainerOptions containerOptions, CancellationToken cancellationToken = default)
        {
            return Create(containerOptions, (dockerClient, containerId) => new AsyncDisposableContainer(dockerClient, containerId), cancellationToken);
        }

        public async Task<string> GetStandardOutput(uint? tail = default, CancellationToken cancellationToken = default)
        {
            using var stream = new MemoryStream();
            await dockerClient.GetContainerOutput(containerId, stream, Stream.Null, tail: tail);
            return stream.ReadContentsAsString();
        }

        public async Task<string> GetStandardError(uint? tail = default, CancellationToken cancellationToken = default)
        {
            using var stream = new MemoryStream();
            await dockerClient.GetContainerOutput(containerId, Stream.Null, stream, tail: tail);
            return stream.ReadContentsAsString();
        }

        protected static async Task<T> Create<T>(ContainerOptions containerOptions, Func<IDockerClient, string, T> factory, CancellationToken cancellationToken) where T : AsyncDisposableContainer
        {
            var dockerClient = new DockerClientConfiguration().CreateClient();

            var containerId = await dockerClient.CreateContainer(containerOptions, cancellationToken);
            await dockerClient.StartContainer(containerId, cancellationToken);
            return factory(dockerClient, containerId);
        }

        public async Task CopyFiles((string SourceFile, string? TargetFile, int Permissions)[] files, CancellationToken cancellationToken = default)
        {
            await dockerClient.CopyFiles(containerId, files, cancellationToken);
        }

        public async Task<long> RunCommand(string command, bool captureOutput = false, CancellationToken cancellationToken = default)
        {
            long exitCode;

            if (captureOutput)
            {
                using var standardOutput = new MemoryStream();
                using var standardError = new MemoryStream();
                exitCode = await dockerClient.RunCommand(containerId, command, standardOutput, standardError, cancellationToken);

                standardOutput.Seek(0, SeekOrigin.Begin);
                standardError.Seek(0, SeekOrigin.Begin);

                LastStandardOutput = standardOutput.ReadContentsAsString();
                LastStandardError = standardError.ReadContentsAsString();
            }
            else
            {
                exitCode = await dockerClient.RunCommand(containerId, command, cancellationToken);
            }

            return exitCode;
        }

        public string? LastStandardOutput { get; private set; }
        public string? LastStandardError { get; private set; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                dockerClient.Dispose();
            }            
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            dockerClient.Dispose();
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            await dockerClient.StopAndRemoveContainer(containerId);
        }
    }
}
