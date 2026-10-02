using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Host;

/// <summary>
/// The DevConsole service graph, built with <c>ValidateOnBuild</c>: a constructor cycle (the "DI cycle that only
/// fails at runtime" CLAUDE.md warns about) or a missing registration fails here instead of at startup.
/// </summary>
[TestFixture]
public class ServiceGraphTests
{
    [Test]
    public void The_game_services_resolve_without_a_cycle()
    {
        var program = typeof(global::DevConsole.Program);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>()).Build());
        foreach (var name in new[] { "ConfigureServices", "ConfigureDataAccess" })
        {
            program.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { services });
        }

        var build = () => services.BuildServiceProvider(new ServiceProviderOptions
            { ValidateOnBuild = true, ValidateScopes = true });

        build.Should().NotThrow();
    }
}
