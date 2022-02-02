using Docker.DotNet;
using System.Threading.Tasks;
using Xunit;

namespace Dockert.IntegrationTests
{
    public class DockerClientExtensionsTests
    {
        [Fact]
        public async Task CanPruneContainers()
        {
            var dockerClient = new DockerClientConfiguration().CreateClient();
            var containerId = await dockerClient.CreateContainer(new ContainerOptions { ImageName = "tianon/true" });
            
            await dockerClient.PruneContainers();
            
            var containers = await dockerClient.ListAllContainers();

            Assert.DoesNotContain(containerId, containers);
        }

        [Fact]
        public async Task CanPruneRunningContainers()
        {
            var dockerClient = new DockerClientConfiguration().CreateClient();
            var containerId = await dockerClient.CreateContainer(new ContainerOptions { ImageName = "alpine", EntryPoint = "sh -c \"while [ 1 ]; do sleep 1; done\"" });
            await dockerClient.StartContainer(containerId);

            await dockerClient.PruneContainers(includeRunning: true);

            var containers = await dockerClient.ListAllContainers();

            Assert.DoesNotContain(containerId, containers);
        }

        [Fact]
        public async Task DoesNotPruneRunningContainersUnlessAsked()
        {
            var dockerClient = new DockerClientConfiguration().CreateClient();
            var containerId = await dockerClient.CreateContainer(new ContainerOptions { ImageName = "alpine", EntryPoint = "sh -c \"while [ 1 ]; do sleep 1; done\"" });
            await dockerClient.StartContainer(containerId);

            await dockerClient.PruneContainers(includeRunning: false);

            var containers = await dockerClient.ListAllContainers();

            Assert.Contains(containerId, containers);

            await dockerClient.PruneContainers(includeRunning: true);

            containers = await dockerClient.ListAllContainers();

            Assert.DoesNotContain(containerId, containers);
        }
    }
}
