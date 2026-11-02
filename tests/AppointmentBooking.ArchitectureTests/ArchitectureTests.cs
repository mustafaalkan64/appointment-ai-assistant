using System.Reflection;
using AppointmentBooking.Domain.Common;
using FluentAssertions;
using MediatR;
using NetArchTest.Rules;
using Xunit;

namespace AppointmentBooking.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Entity<>).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(AppointmentBooking.Application.Common.IEndpoint).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(AppointmentBooking.Infrastructure.Persistence.ApplicationDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_ShouldNotHaveDependencyOn_OtherProjects()
    {
        var otherProjects = new[]
        {
            "AppointmentBooking.Application",
            "AppointmentBooking.Infrastructure",
            "AppointmentBooking.Api",
            "AppointmentBooking.McpServer",
            "AppointmentBooking.ChatApi"
        };

        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(otherProjects)
            .GetResult();

        result.IsSuccessful.Should().BeTrue("Domain layer must be completely independent of other project layers.");
    }

    [Fact]
    public void Application_ShouldNotHaveDependencyOn_InfrastructureOrApi()
    {
        var forbiddenProjects = new[]
        {
            "AppointmentBooking.Infrastructure",
            "AppointmentBooking.Api",
            "AppointmentBooking.McpServer",
            "AppointmentBooking.ChatApi"
        };

        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenProjects)
            .GetResult();

        result.IsSuccessful.Should().BeTrue("Application layer must not depend on Infrastructure, Api, McpServer, or ChatApi.");
    }

    [Fact]
    public void Infrastructure_ShouldNotHaveDependencyOn_ApiProjects()
    {
        var forbiddenProjects = new[]
        {
            "AppointmentBooking.Api",
            "AppointmentBooking.McpServer",
            "AppointmentBooking.ChatApi"
        };

        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenProjects)
            .GetResult();

        result.IsSuccessful.Should().BeTrue("Infrastructure layer must not depend on Web API projects.");
    }

    [Fact]
    public void Handlers_ShouldResideInApplicationLayer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .Should()
            .ResideInNamespace("AppointmentBooking.Application.Features")
            .GetResult();

        result.IsSuccessful.Should().BeTrue("All MediatR request handlers must reside in Application.Features vertical slices.");
    }

    [Fact]
    public void DomainEvents_ShouldImplementIDomainEvent()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .ResideInNamespaceEndingWith("Events")
            .And()
            .AreClasses()
            .Should()
            .ImplementInterface(typeof(IDomainEvent))
            .GetResult();

        result.IsSuccessful.Should().BeTrue("All Domain Event classes must implement IDomainEvent.");
    }
}
