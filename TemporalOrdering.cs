using System.Globalization;
using System.Text.RegularExpressions;

public sealed record TemporalConstraint(string FromId, string Rel, string ToId, string Cue, string Evidence, string Source);
public sealed record UnresolvedTemporalOrder(string FromId, string ToId, string Reason);
public sealed record TemporalOrderConflict(TemporalConstraint Constraint, string Reason);
public sealed record TemporalPlacement(string SegmentId, int LayoutOrder, bool HasUnresolvedOrder);
public sealed record TemporalOrderingInfo(string Method, IReadOnlyList<TemporalConstraint> Constraints,
    IReadOnlyList<UnresolvedTemporalOrder> Unresolved, IReadOnlyList<TemporalOrderConflict> Conflicts,
    IReadOnlyList<TemporalPlacement> Placements,
    string LayoutBasis = "partial_order_with_text_order_tiebreak");

internal sealed record TemporalOrderingResult(TemporalOrderingInfo Info, IReadOnlyList<TimeRelation> SegmentRelations);

internal static class TymTemporalOrdering
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly Regex Immediate = new(@"^(?:immediately afterwards?|immediately after that|right afterwards?|imediat dup[\u0103a] aceea|imediat apoi)\b", Options);
    private static readonly Regex Backward = new(@"^(?:years? earlier|months? earlier|days? earlier|earlier|long ago|back then|in the past|cu (?:\d+|un|doi|dou[\u0103a]|trei|mul[\u021bt]i)?\s*(?:ani|an|luni|lun[\u0103a]|zile|zi|ore|or[\u0103a]) [\u00eei]n urm[\u0103a]|odinioar[\u0103a]|demult|[\u00eei]n trecut|mai devreme)\b", Options);
    private static readonly Regex Forward = new(@"^(?:(?:(?:\d+|one|two|three|several|many)\s+)?(?:years?|months?|weeks?|days?|hours?|minutes?) later|later|afterwards?|subsequently|and then|then|the (?:next|following) (?:day|morning|evening|week|month|year)|mai t[\u00e2a]rziu|a doua zi|[\u00eei]n (?:ziua|diminea[\u021bt]a|seara|s[\u0103a]pt[\u0103a]m[\u00e2a]na|luna) urm[\u0103a]toare|dup[\u0103a] (?:\d+|un|o|doi|dou[\u0103a]|trei|c[\u00e2a]teva) (?:ani|an|luni|lun[\u0103a]|zile|zi|ore|or[\u0103a])|[\u0219s]i apoi|apoi|dup[\u0103a] aceea|pe urm[\u0103a])\b", Options);
    private static readonly Regex Parallel = new(@"^(?:meanwhile|at the same time|in the meantime|simultaneously|[\u00eei]ntre timp|[\u00eei]n acela[\u0219s]i timp|simultan)\b", Options);
    private static readonly Regex Return = new(@"^(?:now|today|acum|ast[\u0103a]zi|azi|back (?:in|to) (?:the )?present|in the present|returning to the present|[\u00eei]napoi [\u00eei]n prezent|revenind [\u00eei]n prezent|[\u00eei]n prezent)\b", Options);
    private static readonly Regex Calendar = new(@"^(?:(?:in|on|[\u00eei]n|[\u00eei]n anul|anul)\s+)?(?<date>[12]\d{3}-\d{2}-\d{2}|[12]\d{3})(?![\d-])\b", Options);

    public static TemporalCue InferCue(string text, bool first)
    {
        if (first) return new("NONE", "", "First event in text order; no predecessor relation.");
        var value = text.TrimStart(' ', '\r', '\n', '\t', '\"', '\'', '(', '\u201c', '\u201e');
        foreach (var (pattern, relation, evidence) in new[]
        {
            (Immediate, "IMMEDIATELY_AFTER", "Explicit immediate-successor cue; not inferred from adjacency."),
            (Backward, "BEFORE", "Leading retrospective cue places the current event before the preceding event."),
            (Forward, "AFTER", "Leading sequential/forward cue indicates succession, not immediate succession."),
            (Parallel, "OVERLAPS", "Leading parallel cue supports overlapping developments, not identical intervals.")
        })
        {
            var match = pattern.Match(value);
            if (match.Success) return new(relation, match.Value, evidence);
        }
        return new("UNKNOWN", "", "No supported temporal evidence; text adjacency is not chronology.");
    }

    public static TemporalOrderingResult Build(IReadOnlyList<MutableNarrativeEvent> events, SegmentationInfo segmentation)
    {
        var decisions = segmentation.Decisions.ToDictionary(item => item.EventId);
        var proposed = new List<TemporalConstraint>();
        var eventSegments = decisions.ToDictionary(item => item.Key, item => item.Value.SegmentId);
        var segmentIds = segmentation.Decisions.Where(item => item.IsBoundary).Select(item => item.SegmentId).ToArray();
        (MutableNarrativeEvent Event, DateOnly Start, DateOnly End)? lastDate = null;
        var knownFlashback = false;
        for (var i = 0; i < events.Count; i++)
        {
            var current = events[i];
            var decision = decisions[current.Id];
            var previous = i == 0 ? null : events[i - 1];
            var scopeChange = decision.Evidence.Any(item => item is "world_mode_change" or "perspective_change");
            if (scopeChange) { lastDate = null; knownFlashback = false; }
            var relation = current.RelationToPrevious;
            var source = "explicit_temporal_cue";
            if (scopeChange) relation = "UNKNOWN";
            if (previous is not null && !scopeChange && knownFlashback
                && decision.Evidence.Contains("narrative_time_return") && Return.IsMatch(current.Text.TrimStart()))
            {
                relation = "AFTER";
                source = "context_return";
                current.RelationEvidence = "Explicit return from a lexically established earlier development.";
                knownFlashback = false;
            }
            if (previous is not null)
            {
                var constraint = relation switch
                {
                    "BEFORE" => new TemporalConstraint(current.Id, "BEFORE", previous.Id, current.RelationCue, current.RelationEvidence, source),
                    "AFTER" => new TemporalConstraint(previous.Id, "BEFORE", current.Id, current.RelationCue, current.RelationEvidence, source),
                    "IMMEDIATELY_AFTER" => new TemporalConstraint(previous.Id, "IMMEDIATELY_BEFORE", current.Id, current.RelationCue, current.RelationEvidence, source),
                    "OVERLAPS" => new TemporalConstraint(previous.Id, "OVERLAPS", current.Id, current.RelationCue, current.RelationEvidence, source),
                    _ => null
                };
                if (constraint is not null) proposed.Add(constraint);
            }
            if (relation == "BEFORE") knownFlashback = true;
            current.RelationToPrevious = previous is null ? "NONE" : relation;
            if (scopeChange) current.RelationEvidence = "Different narrative world or perspective; cross-context order is unresolved.";

            // Only explicit leading calendar expressions are evidence. Inherited labels, weekdays,
            // tense, upload dates and a repeated year do not establish event-event ordering.
            var calendarText = current.Text.TrimStart();
            var cue = InferCue(calendarText, false).Cue;
            if (cue.Length > 0) calendarText = calendarText[cue.Length..].TrimStart(' ', ',', ':');
            var date = Calendar.Match(calendarText);
            if (date.Success && TryCalendar(date.Groups["date"].Value, out var start, out var end))
            {
                if (lastDate is { } prior && (prior.End < start || end < prior.Start))
                {
                    var before = prior.End < start ? prior.Event.Id : current.Id;
                    var after = prior.End < start ? current.Id : prior.Event.Id;
                    proposed.Add(new(before, "BEFORE", after, date.Value,
                        "Disjoint explicit calendar intervals in the same narrative context.", "explicit_calendar"));
                    if (previous?.Id == prior.Event.Id && relation == "UNKNOWN")
                    {
                        current.RelationToPrevious = before == current.Id ? "BEFORE" : "AFTER";
                        current.RelationEvidence = "Disjoint explicit calendar intervals.";
                    }
                }
                lastDate = (current, start, end);
            }
            else if (decision.Evidence.Any(item => item is "flashback_entry" or "narrative_time_return" or "parallel_time_frame" or "forward_time_jump"))
                lastDate = null;
        }

        var conflicts = new List<TemporalOrderConflict>();
        var accepted = RemoveCycles(events.Select(item => item.Id).ToArray(), proposed.Distinct().ToList(), conflicts);
        // OVERLAPS cannot also be BEFORE in either direction, including through a chain.
        var beforeGraph = Graph(events.Select(item => item.Id).ToArray(), accepted.Where(IsOrdered));
        foreach (var overlap in accepted.Where(item => item.Rel == "OVERLAPS").ToArray())
        {
            var from = overlap.FromId;
            var to = overlap.ToId;
            var following = ReachableNodes(beforeGraph, from);
            if (!following.Contains(to))
            {
                (from, to) = (to, from);
                following = ReachableNodes(beforeGraph, from);
                if (!following.Contains(to)) continue;
            }
            var preceding = ReachableNodes(Reverse(beforeGraph), to);
            var incompatible = accepted.Where(item => IsOrdered(item)
                && following.Contains(item.FromId) && preceding.Contains(item.ToId)).Append(overlap).ToArray();
            foreach (var constraint in incompatible)
            {
                accepted.Remove(constraint);
                conflicts.Add(new(constraint, "Overlap conflicts with strict precedence; conflicting claims are not exported."));
            }
            beforeGraph = Graph(events.Select(item => item.Id).ToArray(), accepted.Where(IsOrdered));
        }
        var adjacent = accepted.Select(item => Pair(item.FromId, item.ToId)).ToHashSet();
        var conflictedPairs = conflicts.Select(item => Pair(item.Constraint.FromId, item.Constraint.ToId)).ToHashSet();
        var unresolved = new List<UnresolvedTemporalOrder>();
        for (var i = 1; i < events.Count; i++)
        {
            if (adjacent.Contains(Pair(events[i - 1].Id, events[i].Id))) continue;
            events[i].RelationToPrevious = "UNKNOWN";
            var reason = conflictedPairs.Contains(Pair(events[i - 1].Id, events[i].Id))
                ? "Conflicting temporal evidence." : "No supported direct temporal relation; source adjacency only.";
            events[i].RelationEvidence = reason;
            unresolved.Add(new(events[i - 1].Id, events[i].Id, reason));
        }
        var projected = accepted.Where(item => eventSegments[item.FromId] != eventSegments[item.ToId])
            .Select(item => item with { FromId = eventSegments[item.FromId], ToId = eventSegments[item.ToId],
                Evidence = $"{item.Evidence} Event constraint {item.FromId} -> {item.ToId}; projected onto TS spans, not a whole-interval assertion." })
            .Distinct().ToList();
        projected = RemoveCycles(segmentIds, projected, conflicts);
        var placements = Place(segmentIds, projected, unresolved, eventSegments);
        return new(new("evidence_partial_order_v1", accepted, unresolved, conflicts, placements),
            projected.Select(item => new TimeRelation(item.FromId, item.Rel, item.ToId, item.Cue, item.Evidence)).ToArray());
    }

    private static bool TryCalendar(string value, out DateOnly start, out DateOnly end)
    {
        if (value.Length == 4 && int.TryParse(value, out var year))
        { start = new(year, 1, 1); end = new(year, 12, 31); return true; }
        var valid = DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start);
        end = start; return valid;
    }

    private static bool IsOrdered(TemporalConstraint constraint) => constraint.Rel is "BEFORE" or "IMMEDIATELY_BEFORE";
    private static (string, string) Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
    private static Dictionary<string, HashSet<string>> Graph(IReadOnlyList<string> ids, IEnumerable<TemporalConstraint> constraints)
    {
        var result = ids.ToDictionary(id => id, _ => new HashSet<string>());
        foreach (var constraint in constraints) result[constraint.FromId].Add(constraint.ToId);
        return result;
    }

    private static HashSet<string> ReachableNodes(Dictionary<string, HashSet<string>> graph, string from)
    {
        var visited = new HashSet<string> { from };
        var pending = new Stack<string>(); pending.Push(from);
        while (pending.TryPop(out var node))
            foreach (var next in graph[node])
            {
                if (visited.Add(next)) pending.Push(next);
            }
        return visited;
    }

    private static Dictionary<string, HashSet<string>> Reverse(Dictionary<string, HashSet<string>> graph)
    {
        var result = graph.Keys.ToDictionary(id => id, _ => new HashSet<string>());
        foreach (var (from, targets) in graph) foreach (var to in targets) result[to].Add(from);
        return result;
    }

    private static List<TemporalConstraint> RemoveCycles(IReadOnlyList<string> ids, List<TemporalConstraint> constraints,
        List<TemporalOrderConflict> conflicts)
    {
        // Iterative strongly connected components avoid recursive stack overflow on book-length inputs.
        var graph = Graph(ids, constraints.Where(IsOrdered));
        var reverse = Reverse(graph);
        var visited = new HashSet<string>();
        var finished = new List<string>();
        foreach (var id in ids)
        {
            var stack = new Stack<(string Id, bool Exit)>(); stack.Push((id, false));
            while (stack.TryPop(out var item))
            {
                if (item.Exit) { finished.Add(item.Id); continue; }
                if (!visited.Add(item.Id)) continue;
                stack.Push((item.Id, true));
                foreach (var next in graph[item.Id]) if (!visited.Contains(next)) stack.Push((next, false));
            }
        }
        var components = new Dictionary<string, int>();
        foreach (var id in finished.AsEnumerable().Reverse())
        {
            if (components.ContainsKey(id)) continue;
            var component = components.Count;
            var stack = new Stack<string>(); stack.Push(id);
            while (stack.TryPop(out var node))
            {
                if (!components.TryAdd(node, component)) continue;
                foreach (var next in reverse[node]) stack.Push(next);
            }
        }
        return constraints.Where(item =>
        {
            if (!IsOrdered(item) || components[item.FromId] != components[item.ToId]) return true;
            conflicts.Add(new(item, "Strict-precedence cycle; this claim is withheld from ordering and export."));
            return false;
        }).ToList();
    }

    private static IReadOnlyList<TemporalPlacement> Place(string[] ids, IReadOnlyList<TemporalConstraint> constraints,
        IReadOnlyList<UnresolvedTemporalOrder> unresolved, Dictionary<string, string> eventSegments)
    {
        var graph = Graph(ids, constraints.Where(IsOrdered));
        var indegree = ids.ToDictionary(id => id, _ => 0);
        foreach (var targets in graph.Values) foreach (var target in targets) indegree[target]++;
        var textOrder = ids.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        var pending = new PriorityQueue<string, int>();
        foreach (var id in ids.Where(id => indegree[id] == 0)) pending.Enqueue(id, textOrder[id]);
        var ordered = new List<string>();
        var ambiguous = unresolved.SelectMany(item => new[] { eventSegments[item.FromId], eventSegments[item.ToId] }).ToHashSet();
        while (pending.TryDequeue(out var id, out _))
        {
            if (ordered.Count > 0 && !graph[ordered[^1]].Contains(id))
            { ambiguous.Add(ordered[^1]); ambiguous.Add(id); }
            ordered.Add(id);
            foreach (var next in graph[id]) if (--indegree[next] == 0) pending.Enqueue(next, textOrder[next]);
        }
        return ordered.Select((id, index) => new TemporalPlacement(id, index + 1, ambiguous.Contains(id))).ToArray();
    }
}
