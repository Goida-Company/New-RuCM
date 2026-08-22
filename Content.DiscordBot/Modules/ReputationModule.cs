using Content.DiscordBot.Governance;
using Discord;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

[Group("репутация", "Репутация и пути участия RUCM")]
public sealed class ReputationModule(
    GovernanceCommunityService community,
    ReputationService reputation,
    ReputationHistoryService history,
    CandidateSelectionService selection) : InteractionModuleBase<SocketInteractionContext>
{
    private const double MinimumTrustDisplayEvidence = 1.0;

    [SlashCommand("профиль", "Показать репутацию, игровую активность и доверие по направлениям")]
    public Task ProfileAsync() => ExecuteAsync(async () =>
    {
        var user = await community.RequireUserAsync(Context.User.Id);
        var profile = await reputation.GetProfileAsync(user.Id);
        var selectedPaths = profile.Paths.Select(value => value.Track).ToHashSet(StringComparer.Ordinal);
        var pathText = profile.Paths.Count == 0
            ? "Пути пока не выбраны. Используйте `/репутация пути`."
            : string.Join("\n", profile.Paths.Select(value =>
                $"{(value.Slot == 1 ? "Основной" : "Дополнительный")}: **{TrackName(value.Track)}**"));
        var trustText = string.Join("\n", ReputationTracks.ServicePaths
            .Where(track => track != ReputationTracks.Support)
            .Select(track =>
        {
            var posterior = profile.Tracks.GetValueOrDefault(track);
            var evidence = posterior?.EvidenceWeight ?? 0.0;
            var pathState = selectedPaths.Contains(track) ? string.Empty : " • путь не выбран";
            if (posterior == null || evidence < MinimumTrustDisplayEvidence)
            {
                return $"• **{TrackName(track)}** — недостаточно данных; вес свидетельств {evidence:F1}{pathState}";
            }

            return $"• **{TrackName(track)}** — {posterior.Score}/1000; нижняя 90% граница {posterior.LowerBound:P0}; " +
                   $"вес свидетельств {evidence:F1}{pathState}";
        }));

        var activity = profile.Activity;
        var embed = new EmbedBuilder()
            .WithTitle($"Репутация • {profile.Name}")
            .WithDescription(
                $"**Общая репутация: {profile.General.Score}/1000**\n" +
                $"Оценка надёжности: {profile.General.Mean:P1}; консервативная 90% граница: {profile.General.LowerBound:P1}.\n\n" +
                "Репутация — статистическая оценка устойчивого поведения, а не сумма очков.")
            .AddField("Игровая активность",
                $"Эффективное время: **{activity.OverallHours:F0} ч**\n" +
                $"Активных недель: **{activity.ActiveWeeks}**\n" +
                $"Возраст аккаунта: **{activity.AccountAgeDays} дн.**\n" +
                $"Индекс активности: **{activity.ActivityIndex:P0}**", true)
            .AddField("Пути участия", pathText, true)
            .AddField("Доверие по направлениям", trustText)
            .WithColor(profile.Suspended ? Color.Red : Color.Blue)
            .WithFooter("RUCM Community Governance • байесовская репутация v2")
            .Build();
        await RespondAsync(embed: embed, ephemeral: true);
    });

    [SlashCommand("пути", "Выбрать один или два направления помощи сообществу")]
    public Task PathsAsync(
        [Summary("основной", "Основной путь участия")]
        [Choice("Модерация", ReputationTracks.Moderation)]
        [Choice("Community Court", ReputationTracks.Jury)]
        [Choice("События", ReputationTracks.Event)]
        [Choice("Контрибьюторство", ReputationTracks.Contributor)] string primary,
        [Summary("дополнительный", "Необязательный второй путь")]
        [Choice("Нет", "none")]
        [Choice("Модерация", ReputationTracks.Moderation)]
        [Choice("Community Court", ReputationTracks.Jury)]
        [Choice("События", ReputationTracks.Event)]
        [Choice("Контрибьюторство", ReputationTracks.Contributor)] string secondary = "none") => ExecuteAsync(async () =>
    {
        var user = await community.RequireUserAsync(Context.User.Id);
        await reputation.SetPathsAsync(user.Id, primary, secondary == "none" ? null : secondary);
        await RespondAsync(
            $"Пути сохранены: **{TrackName(primary)}**" +
            (secondary == "none" ? "." : $" + **{TrackName(secondary)}**."),
            ephemeral: true);
    });

    [SlashCommand("прогресс", "Показать прогресс автоматической квалификации по выбранным путям")]
    public Task ProgressAsync() => ExecuteAsync(async () =>
    {
        var user = await community.RequireUserAsync(Context.User.Id);
        var rows = await selection.QualificationProgressAsync(user.Id);
        if (rows.Count == 0)
        {
            await RespondAsync("Пути участия пока не выбраны. Используйте `/репутация пути`.", ephemeral: true);
            return;
        }

        var fields = new List<EmbedFieldBuilder>();
        foreach (var row in rows)
        {
            var current = RomanLevel(row.CurrentLevel);
            var eligible = RomanLevel(row.EligibleLevel);
            string body;
            if (row.NextLevel == null)
            {
                body =
                    $"Текущий уровень: **{current}**\n" +
                    $"LB90: **{row.LowerBound:P1}** • effective evidence: **{row.EvidenceWeight:F2}** • завершено: **{row.CompletedAssignments}**\n" +
                    "Достигнут максимальный уровень IV.";
            }
            else
            {
                var requiredLower = row.RequiredLowerBound ?? throw new InvalidOperationException("Не задан порог LB90 квалификации.");
                var requiredEvidence = row.RequiredEvidenceWeight ?? throw new InvalidOperationException("Не задан порог evidence квалификации.");
                var requiredCompleted = row.RequiredCompletedAssignments ?? throw new InvalidOperationException("Не задан порог завершённых обязанностей квалификации.");
                var lowerOk = row.LowerBound >= requiredLower;
                var evidenceOk = row.EvidenceWeight >= requiredEvidence;
                var completedOk = row.CompletedAssignments >= requiredCompleted;
                var additionalForLower = QualificationProjection.AdditionalPositiveEvidenceForLowerBound(
                    row.Score,
                    row.EvidenceWeight,
                    requiredLower);
                var additionalForEvidence = Math.Max(0, requiredEvidence - row.EvidenceWeight);
                var estimatedAdditionalPositive = Math.Max(additionalForLower, additionalForEvidence);

                body =
                    $"Текущий уровень: **{current}** • расчётный допустимый: **{eligible}**\n" +
                    $"До **{RomanLevel(row.NextLevel.Value)}**:\n" +
                    $"{Mark(lowerOk)} LB90 **{row.LowerBound:P1} / {requiredLower:P0}**\n" +
                    $"{Mark(evidenceOk)} effective evidence **{row.EvidenceWeight:F2} / {requiredEvidence:F0}**\n" +
                    $"{Mark(completedOk)} завершённые обязанности **{row.CompletedAssignments} / {requiredCompleted}**";

                if ((!lowerOk || !evidenceOk) && double.IsFinite(estimatedAdditionalPositive))
                {
                    body +=
                        $"\n📈 Оценочно до статистических условий: **+{estimatedAdditionalPositive:F1} effective positive evidence** " +
                        "при отсутствии новых отрицательных событий.";
                }

                if (row.EligibleLevel > row.CurrentLevel)
                    body += "\n✅ Условия повышения уже выполнены; Reputation Coordinator применит новый уровень.";
            }

            fields.Add(new EmbedFieldBuilder()
                .WithName(TrackName(row.Track))
                .WithValue(body));
        }

        var embed = new EmbedBuilder()
            .WithTitle("Прогресс квалификации")
            .WithDescription(
                "Квалификация повышается только когда одновременно выполнены консервативная 90% граница, " +
                "effective evidence и минимальное число завершённых обязанностей. Однотипные действия имеют убывающий вес `1/√n` в пределах одного дня; устойчивые действия в разные дни снова получают полный базовый вес. " +
                "Прогноз дополнительного evidence справочный и не учитывает будущий decay или новые отрицательные события.")
            .WithColor(Color.Blue);
        foreach (var field in fields)
            embed.AddField(field);
        embed.WithFooter("I → II: 65% / 4 / 4 • II → III: 75% / 10 / 10 • III → IV: 85% / 20 / 20");
        await RespondAsync(embed: embed.Build(), ephemeral: true);
    });

    [SlashCommand("история", "Показать последние статистические события репутации")]
    public Task HistoryAsync() => ExecuteAsync(async () =>
    {
        var user = await community.RequireUserAsync(Context.User.Id);
        var rows = await history.GetAsync(user.Id, 25);
        var description = rows.Count == 0
            ? "Значимых репутационных наблюдений пока нет. Игровая активность всё равно участвует в базовой оценке."
            : string.Join("\n", rows.Select(value =>
            {
                var signal = value.SuccessWeight > 0 && value.FailureWeight > 0
                    ? $"+{value.SuccessWeight:F2} / −{value.FailureWeight:F2}"
                    : value.SuccessWeight > 0
                        ? $"+{value.SuccessWeight:F2}"
                        : $"−{value.FailureWeight:F2}";
                var auditOnly = ReputationMath.IsAuthoritativeReason(value.Reason)
                    ? string.Empty
                    : " • _архив, не участвует в расчёте v2_";
                return $"• <t:{new DateTimeOffset(value.OccurredAt).ToUnixTimeSeconds()}:d> " +
                       $"**{TrackName(value.Track)}** • {ReasonName(value.Reason)} • `{signal}`" +
                       (value.SeriousNegative ? " ⚠️" : string.Empty) + auditOnly;
            }));
        if (description.Length > 3900)
            description = description[..3900] + "…";
        await RespondAsync(embed: new EmbedBuilder()
            .WithTitle("История репутации")
            .WithDescription(description)
            .WithColor(Color.DarkBlue)
            .WithFooter("Архивные события старой системы сохраняются для аудита, но не входят в Reputation v2. Вес актуальных событий уменьшается со временем; серьёзные ошибки реабилитируются устойчивым хорошим поведением.")
            .Build(), ephemeral: true);
    });

    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (CourtRuleException exception)
        {
            if (Context.Interaction.HasResponded)
                await FollowupAsync(exception.Message, ephemeral: true);
            else
                await RespondAsync(exception.Message, ephemeral: true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Reputation command failed for {Context.User.Id}", exception);
            const string message = "Не удалось выполнить действие с репутацией. Ошибка записана в журнал Discord-бота.";
            if (Context.Interaction.HasResponded)
                await FollowupAsync(message, ephemeral: true);
            else
                await RespondAsync(message, ephemeral: true);
        }
    }

    private static string Mark(bool value) => value ? "✅" : "❌";

    private static string RomanLevel(short level) => level switch
    {
        <= 0 => "—",
        1 => "I",
        2 => "II",
        3 => "III",
        >= 4 => "IV",
    };

    private static string TrackName(string track) => track switch
    {
        ReputationTracks.General => "Общая",
        ReputationTracks.Support => "Модерация",
        ReputationTracks.Moderation => "Модерация",
        ReputationTracks.Jury => "Community Court",
        ReputationTracks.Event => "События",
        ReputationTracks.Contributor => "Контрибьюторство",
        _ => track,
    };

    private static string ReasonName(string reason) => reason switch
    {
        ReputationReasons.AHelpResolved => "успешно обработан AHelp",
        ReputationReasons.DutyCompleted => "дежурство завершено",
        ReputationReasons.DutyFailed => "дежурство сорвано",
        ReputationReasons.JuryCompleted => "обязанность присяжного выполнена",
        ReputationReasons.JuryFailed => "принятая обязанность присяжного не выполнена",
        ReputationReasons.EventReviewCompleted => "рецензия события завершена",
        ReputationReasons.EventReviewFailed => "принятая рецензия не завершена",
        ReputationReasons.EventSessionCompleted => "событие корректно завершено",
        ReputationReasons.EventSessionAborted => "событие аварийно завершено",
        ReputationReasons.ModerationReviewCompleted => "независимый аудит завершён",
        ReputationReasons.ModerationReviewFailed => "принятый аудит не завершён",
        ReputationReasons.ModerationActionCorrect => "действие подтверждено аудитом",
        ReputationReasons.ModerationActionMinorIssue => "в действии найдены недостатки",
        ReputationReasons.ModerationActionWrong => "серьёзная ошибка модерации",
        ReputationReasons.FalseReport => "заведомо ложная жалоба",
        ReputationReasons.ContributionAccepted => "подтверждён вклад в проект",
        _ when reason.StartsWith("legacy:", StringComparison.Ordinal) => "архивное событие старой системы",
        _ => reason,
    };
}
