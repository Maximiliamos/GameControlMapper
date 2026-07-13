using Xunit;

namespace GameControlMapper.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingSensitiveIntegrationCollection
{
    public const string Name = "Timing-sensitive integration";
}
