using Docker.DotNet;
using System.Threading.Tasks;
using Xunit;

namespace Dockert.IntegrationTests
{
    public class AsyncDisposableContainerTests
    {
        [Fact]
        public async Task CanCreateContainerFromImage()
        {
            await using var container = await AsyncDisposableContainer.FromImage("tianon/true");
        }

        [Fact]
        public async Task CanCaptureStandardOutputFromContainer()
        {
            await using var container = await AsyncDisposableContainer.FromImage(new ContainerOptions { ImageName = "alpine", EntryPoint = "sh -c \"echo -n hi\"" });
            Assert.Equal("hi", await container.GetStandardOutput());
        }

        [Fact]
        public async Task CanCaptureStandardErrorFromContainer()
        {
            await using var container = await AsyncDisposableContainer.FromImage(new ContainerOptions { ImageName = "alpine", EntryPoint = "sh -c \"echo -n hi 1>&2\"" });

            Assert.Equal("hi", await container.GetStandardError());
        }

        [Fact]
        public async Task CanRunCommandInRunningContainer()
        {
            await using var container = await AsyncDisposableContainer.FromImage(new ContainerOptions { ImageName = "alpine", EntryPoint = "sh -c \"while [ 1 ]; do sleep 1; done\"" });
            var exitCode = await container.RunCommand("echo -n hi");

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public async Task CanCaptureStandardOutputFromCommandInRunningContainer()
        {
            await using var container = await AsyncDisposableContainer.FromImage(new ContainerOptions { ImageName = "alpine", EntryPoint = "sh -c \"while [ 1 ]; do sleep 1; done\"" });
            var exitCode = await container.RunCommand("echo -n hi", true);

            Assert.Equal("hi", container.LastStandardOutput);
        }

        [Fact]
        public async Task CanCaptureStandardErrorFromCommandInRunningContainer()
        {
            await using var container = await AsyncDisposableContainer.FromImage(new ContainerOptions { ImageName = "alpine", EntryPoint = "sh -c \"while [ 1 ]; do sleep 1; done\"" });
            var exitCode = await container.RunCommand("sh -c \"echo -n hi 1>&2\"", true);

            Assert.Equal("hi", container.LastStandardError);
        }

        [Fact]
        public async Task CanStopAndRemoveRunningContainer()
        {
            var containerId = await StartAndDisposeRunningContainer();
            var dockerClient = new DockerClientConfiguration().CreateClient();
            var containers = await dockerClient.ListAllContainers();

            Assert.DoesNotContain(containerId, containers);
        }

        [Fact]
        private async Task<string> StartAndDisposeRunningContainer()
        {
            await using var container = await AsyncDisposableContainer.FromImage(new ContainerOptions { ImageName = "alpine", EntryPoint = "sh -c \"while [ 1 ]; do sleep 1; done\"" });
            return container.Id;
        }
    }
}