using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace LabScaner.Integration.Tests.Infrastructure;

/// <summary>
/// Тестовый почтовый сервер GreenMail (IMAP 3143, SMTP 3025, без шифрования) с одним ящиком.
/// Вход по логину <see cref="Login"/>, адрес — <see cref="Address"/>.
/// </summary>
public sealed class GreenMailFixture : IAsyncLifetime
{
    public const string Login = "teacher";
    public const string Password = "Gm-Pa55word-xyz";
    public const string Address = "teacher@example.edu";

    private readonly IContainer _container = new ContainerBuilder("greenmail/standalone:2.1.14")
        .WithPortBinding(3143, true)
        .WithPortBinding(3025, true)
        .WithEnvironment("GREENMAIL_OPTS",
            "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 " +
            "-Dgreenmail.tls.keystore.file=/home/greenmail/greenmail.p12 -Dgreenmail.tls.keystore.password=changeit " +
            $"-Dgreenmail.users={Login}:{Password}@example.edu")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(3143).UntilExternalTcpPortIsAvailable(3025))
        .Build();

    public string Host => _container.Hostname;

    public int ImapPort => _container.GetMappedPublicPort(3143);

    public int SmtpPort => _container.GetMappedPublicPort(3025);

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
