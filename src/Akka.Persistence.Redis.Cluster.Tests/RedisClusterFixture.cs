// -----------------------------------------------------------------------
// <copyright file="RedisClusterFixture.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Akka.Util;
using Docker.DotNet;
using Docker.DotNet.Models;
using Xunit;

namespace Akka.Persistence.Redis.Cluster.Tests
{
    [CollectionDefinition("RedisClusterSpec")]
    public sealed class RedisSpecsFixture : ICollectionFixture<RedisClusterFixture>
    {
    }

    public class RedisClusterFixture : IAsyncLifetime
    {
        protected readonly string RedisContainerName = $"redis-cluster-{Guid.NewGuid():N}";
        protected readonly string RedisContainerName2 = $"redis-cluster-{Guid.NewGuid():N}";
        protected DockerClient Client;

        public RedisClusterFixture()
        {
            DockerClientConfiguration config;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                config = new DockerClientConfiguration(new Uri("unix://var/run/docker.sock"));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                config = new DockerClientConfiguration(new Uri("npipe://./pipe/docker_engine"));
            else
                throw new NotSupportedException($"Unsupported OS [{RuntimeInformation.OSDescription}]");

            Client = config.CreateClient();
        }

        protected string ImageName => "grokzen/redis-cluster";
        protected string Tag => "latest";
        protected string RedisImageName => $"{ImageName}:{Tag}";

        public string ConnectionString { get; private set; }

        public async Task InitializeAsync()
        {
            var images = await Client.Images.ListImagesAsync(new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    {"reference", new Dictionary<string, bool> {{RedisImageName, true}}}
                }
            });
            if (images.Count == 0)
                await Client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = RedisImageName, Tag = "latest" }, null,
                    new Progress<JSONMessage>(message =>
                    {
                        Console.WriteLine(!string.IsNullOrEmpty(message.ErrorMessage)
                            ? message.ErrorMessage
                            : $"{message.ID} {message.Status} {message.ProgressMessage}");
                    }));

            var redisHostPort1 = ThreadLocalRandom.Current.Next(9000, 10000);
            var redisHostPort2 = ThreadLocalRandom.Current.Next(9000, 10000);

            await CreateContainer(redisHostPort1, RedisContainerName);
            await CreateContainer(redisHostPort2, RedisContainerName2);

            ConnectionString = $"127.0.0.1:{redisHostPort1},127.0.0.1:{redisHostPort2}";
        }
        private async ValueTask CreateContainer(int redisHostPort, string redisContainerName)
        {

            // create the container
            await Client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = RedisImageName,
                Name = redisContainerName,
                Tty = true,
                Env = new List<string> { "IP=0.0.0.0", $"INITIAL_PORT={redisHostPort}" },
                ExposedPorts =
                    new Dictionary<string, EmptyStruct>
                    {
                        {$"{redisHostPort}/tcp", new EmptyStruct()},
                        {$"{redisHostPort + 1}/tcp", new EmptyStruct()},
                        {$"{redisHostPort + 2}/tcp", new EmptyStruct()},
                        {$"{redisHostPort + 3}/tcp", new EmptyStruct()},
                        {$"{redisHostPort + 4}/tcp", new EmptyStruct()},
                        {$"{redisHostPort + 5}/tcp", new EmptyStruct()}
                    },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        {
                            $"{redisHostPort}/tcp",
                            new List<PortBinding> {new PortBinding {HostPort = $"{redisHostPort}"}}
                        },
                        {
                            $"{redisHostPort + 1}/tcp",
                            new List<PortBinding> {new PortBinding {HostPort = $"{redisHostPort + 1}"}}
                        },
                        {
                            $"{redisHostPort + 2}/tcp",
                            new List<PortBinding> {new PortBinding {HostPort = $"{redisHostPort + 2}"}}
                        },
                        {
                            $"{redisHostPort + 3}/tcp",
                            new List<PortBinding> {new PortBinding {HostPort = $"{redisHostPort + 3}"}}
                        },
                        {
                            $"{redisHostPort + 4}/tcp",
                            new List<PortBinding> {new PortBinding {HostPort = $"{redisHostPort + 4}"}}
                        },
                        {
                            $"{redisHostPort + 5}/tcp",
                            new List<PortBinding> {new PortBinding {HostPort = $"{redisHostPort + 5}"}}
                        }
                    }
                }
            });

            // start the container
            await Client.Containers.StartContainerAsync(redisContainerName, new ContainerStartParameters());

            // Provide a 10 second startup delay
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        public async Task DisposeAsync()
        {
            if (Client != null)
            {
                // Delay to make sure that all tests has completed cleanup.
                await Task.Delay(TimeSpan.FromSeconds(5));

                await KillContainer(RedisContainerName);
                await KillContainer(RedisContainerName2);
                Client.Dispose();
            }
        }
        private async ValueTask KillContainer(string container)
        {
            // Kill the container, we can't simply stop the container because Redis can hung indefinetly
            // if we simply stop the container.
            await Client.Containers.KillContainerAsync(container, new ContainerKillParameters());

            await Client.Containers.RemoveContainerAsync(container,
                new ContainerRemoveParameters { Force = true });
        }
    }
}