using CvManager.Agents.Agents;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CvManager.Agents.Tests;

/// <summary>
/// The deterministic two-turn contract from issue 0016 / P1T-82: a follow-up runs with the prior
/// turn's question and answer replayed ahead of it, so the model actually sees the context.
/// </summary>
public class RosterQaThreadedTests
{
    [Fact]
    public async Task A_followup_replays_the_prior_turn_to_the_model()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace knows React.")),
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada is free in July.")));
        var agent = new RosterQaAgent(chat, new FakeToolSource(), NullLoggerFactory.Instance);
        var store = new RosterQaThreadStore(TimeProvider.System);

        var thread = store.Resolve(null, null);
        var first = await agent.AskAsync("Who knows React?", thread.History);
        store.Append(null, thread.ThreadId, "Who knows React?", first.Text);

        var resumed = store.Resolve(null, thread.ThreadId);
        await agent.AskAsync("And which of them are free in July?", resumed.History);

        var secondTurn = chat.ReceivedMessages[^1];
        var texts = secondTurn.Select(m => m.Text).ToList();
        texts.Should().ContainInOrder(
            "Who knows React?",
            "Ada Lovelace knows React.",
            "And which of them are free in July?");
        secondTurn.First(m => m.Text == "Ada Lovelace knows React.").Role
            .Should().Be(ChatRole.Assistant, "the prior answer replays as an assistant turn");
    }

    [Fact]
    public async Task The_single_turn_path_still_sends_only_the_question()
    {
        var chat = new FakeChatClient(
            () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        var agent = new RosterQaAgent(chat, new FakeToolSource(), NullLoggerFactory.Instance);

        await agent.AskAsync("Who knows React?");

        chat.ReceivedMessages[^1].Count(m => m.Role == ChatRole.User).Should().Be(1);
    }
}
