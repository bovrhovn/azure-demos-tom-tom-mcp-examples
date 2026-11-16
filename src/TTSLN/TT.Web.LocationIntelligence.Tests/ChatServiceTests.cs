using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using TT.Web.LocationIntelligence.Features.Chat;

namespace TT.Web.LocationIntelligence.Tests;

[TestClass]
public sealed class ChatServiceTests
{
    private readonly TestStore store = new();
    private readonly Mock<IChatResponder> responder = new();
    private readonly Mock<IClientProxy> notifications = new();

    private ChatService Service(ChatOptions? options = null)
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(client => client.Group("owner")).Returns(notifications.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.SetupGet(context => context.Clients).Returns(clients.Object);
        return new(store, responder.Object, hub.Object, Options.Create(options ?? new()), NullLogger<ChatService>.Instance);
    }

    private Task<ChatSession> CreateSession() => store.CreateAsync(new() { OwnerId = "owner", Title = "Routes" }, default);

    [TestMethod]
    public async Task PersistsBothMessagesAndActualUsageAndDeduplicatesRetries()
    {
        var session = await CreateSession();
        responder.Setup(provider => provider.ReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentReply("Take the A10.", "test-deployment", 27, 11));
        var service = Service();
        var request = new SendMessageRequest(Guid.NewGuid(), "Plan a route.");

        await service.SendAsync("owner", session.Id, request, default);
        await service.SendAsync("owner", session.Id, request, default);
        var saved = await store.GetAsync("owner", session.Id, default);

        saved!.Messages.Select(message => message.Role).Should().Equal("user", "assistant");
        saved.Messages[0].Content.Should().Be("Plan a route.");
        saved.Messages[1].Should().BeEquivalentTo(new
        {
            Content = "Take the A10.",
            Model = "test-deployment",
            InputTokens = (long?)27,
            OutputTokens = (long?)11,
            Status = "completed"
        });
        saved.BusyUntil.Should().BeNull();
        responder.Verify(provider => provider.ReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(proxy => proxy.SendCoreAsync("ConversationChanged", It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task ProviderFailurePersistsExplicitFailureAndReleasesLease()
    {
        var session = await CreateSession();
        responder.Setup(provider => provider.ReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("private upstream error"));

        var action = () => Service().SendAsync("owner", session.Id, new(Guid.NewGuid(), "Find a place."), default);
        (await action.Should().ThrowAsync<ChatException>()).Which.StatusCode.Should().Be(502);
        var saved = await store.GetAsync("owner", session.Id, default);

        saved!.Messages.Should().HaveCount(2);
        saved.Messages[1].Status.Should().Be("failed");
        saved.Messages[1].Content.Should().NotContain("private upstream error");
        saved.BusyUntil.Should().BeNull();
    }

    [TestMethod]
    public async Task ConcurrentSendIsRejectedWithoutOverwritingFirstTurn()
    {
        var session = await CreateSession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        responder.Setup(provider => provider.ReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { entered.SetResult(); return release.Task; });
        var service = Service();
        var first = service.SendAsync("owner", session.Id, new(Guid.NewGuid(), "First"), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var second = () => service.SendAsync("owner", session.Id, new(Guid.NewGuid(), "Second"), default);
            (await second.Should().ThrowAsync<ChatException>()).Which.StatusCode.Should().Be(409);
        }
        finally { release.SetResult(new("Answer", "model", null, null)); }
        await first;
        var saved = await store.GetAsync("owner", session.Id, default);
        saved!.Messages.Select(message => message.Content).Should().Equal("First", "Answer");
    }

    [TestMethod]
    public async Task ForeignOwnerCannotReadOrAppendConversation()
    {
        var session = await CreateSession();
        var action = () => Service().SendAsync("other-owner", session.Id, new(Guid.NewGuid(), "Hello"), default);
        (await action.Should().ThrowAsync<ChatException>()).Which.StatusCode.Should().Be(404);
        responder.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("     ")]
    [DataRow("Too long")]
    public async Task InvalidInputIsRejectedBeforePersistence(string content)
    {
        var session = await CreateSession();
        var action = () => Service(new() { MaxMessageLength = 5 }).SendAsync(
            "owner", session.Id, new(Guid.NewGuid(), content), default);
        (await action.Should().ThrowAsync<ChatException>()).Which.StatusCode.Should().Be(400);
        (await store.GetAsync("owner", session.Id, default))!.Messages.Should().BeEmpty();
    }

    [TestMethod]
    public async Task NextTurnIncludesCompletedHistoryButExcludesFailedAssistantReplies()
    {
        var session = await CreateSession();
        responder.SetupSequence(provider => provider.ReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Offline"))
            .ReturnsAsync(new AgentReply("Amsterdam", "model", null, null));
        var service = Service();
        try { await service.SendAsync("owner", session.Id, new(Guid.NewGuid(), "Where?"), default); }
        catch (ChatException) { }
        await service.SendAsync("owner", session.Id, new(Guid.NewGuid(), "Try again"), default);
        responder.Verify(provider => provider.ReplyAsync(It.Is<IReadOnlyList<ChatMessage>>(messages =>
            messages.Count == 2 && messages[0].Content == "Where?" && messages[1].Content == "Try again"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task EscapedUnicodeIsBoundedBelowCosmosItemLimitBeforeSaving()
    {
        var session = await CreateSession();
        var large = session with
        {
            Messages = Enumerable.Range(0, 20).Select(index => new ChatMessage(
                index.ToString(), "assistant", new string('\u4e00', 16000), DateTimeOffset.UtcNow)).ToList()
        };
        await store.ReplaceAsync(large, default);
        var action = () => Service().SendAsync("owner", session.Id, new(Guid.NewGuid(), "One more"), default);
        (await action.Should().ThrowAsync<ChatException>()).Which.StatusCode.Should().Be(400);
        responder.VerifyNoOtherCalls();
    }
}
