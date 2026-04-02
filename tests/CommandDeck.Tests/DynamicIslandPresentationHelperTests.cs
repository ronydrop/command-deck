using CommandDeck.Helpers;
using CommandDeck.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommandDeck.Tests;

[TestClass]
public class DynamicIslandPresentationHelperTests
{
    [TestMethod]
    public void WaitingInput_BuildsQuestionKindAndChoiceSummary()
    {
        var choices = new[]
        {
            new AiAgentChoiceOption("Production", "1\r\n"),
            new AiAgentChoiceOption("Staging", "2\r\n"),
            new AiAgentChoiceOption("Local only", "3\r\n")
        };

        var kind = DynamicIslandPresentationHelper.GetEventKind(AiAgentState.WaitingInput);
        var headline = DynamicIslandPresentationHelper.BuildHeadline("Codex", AiAgentState.WaitingInput, "Which deployment target?");
        var secondary = DynamicIslandPresentationHelper.BuildSecondarySnippet(AiAgentState.WaitingInput, "Which deployment target?", null, choices);

        Assert.AreEqual(DynamicIslandEventKind.Question, kind);
        Assert.AreEqual("Codex fez uma pergunta", headline);
        StringAssert.Contains(secondary, "Production");
        StringAssert.Contains(secondary, "Staging");
    }

    [TestMethod]
    public void Executing_PrefersActionDetailForPrimarySnippet()
    {
        var primary = DynamicIslandPresentationHelper.BuildPrimarySnippet(
            AiAgentState.Executing,
            "Read (ferramenta)",
            "src/auth/middleware.ts",
            Array.Empty<AiAgentChoiceOption>());

        Assert.AreEqual("src/auth/middleware.ts", primary);
    }

    [TestMethod]
    public void Error_MapsToDangerTone()
    {
        var tone = DynamicIslandPresentationHelper.GetVisualTone(AiAgentState.Error, NotificationType.Error);

        Assert.AreEqual(DynamicIslandVisualTone.Danger, tone);
    }
}
