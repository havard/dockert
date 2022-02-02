﻿# Dockert 🐳🍵

Dockert 🐳🍵 makes testing with Docker in .NET simpler.

Need a database? Dockert will help you easily test that database, across all platforms.

Have an unwieldy third party dependency? Containerize it!

Doing micro services in the cloud? We feel you. We hear you. 

We're here to make it simpler. Because more whales means more tea. Or something.

## Getting started

Here's how you start a container in a test:

```
// We use an instance of DockerClient from Docker.DotNet to pull an image, create and start a container
var containerId = await dockerClient.CreateContainer(imageName, environmentVariables, portBindings);
await dockerClient.StartContainer(containerId);
```
Now you write your arrange/act/asserts as usual, and then when you're done, just:
```
await dockerClient.StopAndRemoveContainer(containerId);
```

Since tests may fail and there's boilerplate involved with handling cleanup easily, we also include
the `AsyncDisposableContainer` for you to simplify even further:

```
await using var container = await AsyncDisposableContainer.FromImage(imageName);
```
You now have a running container and can arrange/act/assert as normal. The container will be stopped
and removed when the test is done, or fails, or something unexpected happens.

You can also subclass `AsyncDisposableContainer` and create a custom container with a simple API for your particular use case.
We recommend using a factory method to instantiate your container, utilizing the `Create` factory method provided:

```
public class MysqlContainer : AsyncDisposableContainer
{
    private MysqlContainer(IDockerClient dockerClient, string containerId)
      : base(dockerClient, containerId)
    {   
    }

    public static async Task<MySqlContainer> WithDatabase(string name)
    {
        var container = Create<MySqlContainer>("mysql:latest", 
            new [] 
            { 
                "MYSQL_ROOT_PASSWORD=MyV3ryS3cretP4ssword!", 
                $"MYSQL_DATABASE={name}"
            },
            (dockerClient, containerId) => new MysqlContainer(dockerClient, containerId));
    }
}
```

## Handling lingering containers
Even if we try our best, unexpected errors may occur. Sometimes containers can't be started, or will start, but then exit, or similar, leaving us with containers Dockert can't clean up properly. To remove these (as best we can), just go to your command line and remove them one by one, or simply:
```
    await dockerClient.PruneContainers(includeRunning: true);
```