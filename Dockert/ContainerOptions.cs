using System;

namespace Dockert
{
    public class ContainerOptions
    {
        public string ImageName { get; set; } = string.Empty;
        public string? EntryPoint { get; set; }
        public string[] EnvironmentVariables { get; set; } = Array.Empty<string>();
        public string[] PortBindings { get; set; } = Array.Empty<string>();
    }
}
