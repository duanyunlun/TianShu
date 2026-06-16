using TianShu.Provider.Abstractions;

[assembly: ProviderBootstrapRegistration(typeof(TianShu.Provider.BootstrapLoaderFixtures.LoadedFixtureBootstrap))]
[assembly: ProviderBootstrapRegistration(typeof(TianShu.Provider.BootstrapLoaderFixtures.DuplicateFixtureBootstrapA))]
[assembly: ProviderBootstrapRegistration(typeof(TianShu.Provider.BootstrapLoaderFixtures.DuplicateFixtureBootstrapB))]

namespace TianShu.Provider.BootstrapLoaderFixtures;

public interface ILoadedFixtureBootstrap
{
    string Key { get; }
}

public sealed class LoadedFixtureBootstrap : ILoadedFixtureBootstrap
{
    public string Key => "loaded-bootstrap";
}

public interface IDuplicateFixtureBootstrap
{
    string Key { get; }
}

public sealed class DuplicateFixtureBootstrapA : IDuplicateFixtureBootstrap
{
    public string Key => "same-key";
}

public sealed class DuplicateFixtureBootstrapB : IDuplicateFixtureBootstrap
{
    public string Key => "same-key";
}
