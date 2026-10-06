using System.Globalization;
using System.Text;

namespace Navislamia.LoadTest;

/// <summary>Writes the run as Markdown: one section per stage, then the bottlenecks the thresholds flag.</summary>
public static class Report
{
    /// <summary>Each loop's interval: a tick longer than it delays the next one.</summary>
    private static readonly Dictionary<string, double> LoopBudgetMs = new()
    {
        ["combat"] = 100, ["creatures"] = 100, ["monster-ai"] = 300, ["monster-movement"] = 500,
        ["ground-items"] = 1000, ["buffs"] = 500, ["casts"] = 50, ["skill-effects"] = 50, ["pets"] = 250,
        ["regeneration"] = 3000,
    };

    public static string Write(LoadOptions o, DateTime started, ServerProbe? probe, List<StageResult> stages)
    {
        var r = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("fr-FR");
        string N(double v, string format = "0") => v.ToString(format, c);

        r.AppendLine($"# Test de charge Navislamia — {started:yyyy-MM-dd HH:mm}");
        r.AppendLine();
        r.AppendLine($"- Étapes : {string.Join(", ", o.Stages)} clients, {o.HoldSeconds} s chacune après une montée de {N(o.RampPerSecond, "0.#")} client/s");
        r.AppendLine($"- Comportement : marche toutes les ~{N(o.MoveInterval, "0.#")} s dans un rayon de {N(o.WalkRadius)}, chat local toutes les ~{N(o.ChatInterval, "0.#")} s, " +
                     (o.Combat ? $"combat contre les monstres à moins de {N(o.HuntRange)} unités ({N(o.AttackSeconds)} s par cible)" : "sans combat"));
        r.AppendLine($"- Serveur observé : {(probe is null ? "aucun (mesures côté client seulement)" : probe.ProcessDescription + (probe.EventPipeAvailable ? ", EventPipe actif" : ", sans EventPipe"))}");
        r.AppendLine($"- Machine : {Environment.ProcessorCount} cœurs logiques ; les bots tournent sur la même machine que le serveur s'il est observé");
        r.AppendLine();
        r.AppendLine("Latences en millisecondes, mesurées par les bots entre l'envoi d'une requête et la réponse du serveur : p50 / p95 / p99 / max.");
        r.AppendLine();

        var findings = new List<string>();
        foreach (var s in stages)
        {
            r.AppendLine($"## {s.Target} clients ({N(s.Seconds)} s)");
            r.AppendLine();
            r.AppendLine($"En jeu à la fin de l'étape : **{s.InWorld} / {s.Target}**{(s.Connecting > 0 ? $" ({s.Connecting} encore en connexion)" : "")}.");
            r.AppendLine();
            r.AppendLine("| Mesure (client) | n | p50 / p95 / p99 / max |");
            r.AppendLine("|---|---:|---|");
            Row(r, "Connexion auth (version → clé à usage unique)", s.AuthLogin);
            Row(r, "Entrée en jeu (TM_CS_LOGIN → TS_SC_LOGIN_RESULT)", s.WorldEntry);
            Row(r, "Écho de marche (TM_CS_MOVE_REQUEST → TS_SC_MOVE)", s.MoveEcho);
            Row(r, "Écho de chat (TM_CS_CHAT_REQUEST → TS_SC_CHAT_LOCAL)", s.ChatEcho);
            Row(r, "Premier coup (TM_CS_ATTACK_REQUEST → TS_SC_ATTACK_EVENT)", s.AttackFirstSwing);
            r.AppendLine();

            var t = s.Counters;
            r.AppendLine($"Trafic : {N(t.FramesReceived / s.Seconds)} trames/s et {N(t.BytesReceived / s.Seconds / 1024, "0.0")} Kio/s reçues par l'ensemble des bots, " +
                         $"{N(t.FramesSent / s.Seconds)} trames/s envoyées. Marches {t.MovesSent} (sans écho en 5 s : {t.MovesLost}), " +
                         $"chats {t.ChatsSent}, attaques {t.AttacksSent}, coups portés {t.SwingsLanded}, monstres tués {t.Kills}, morts {t.Deaths}.");
            r.AppendLine();

            if (s.Server is { } srv) WriteServer(r, srv, s, N);

            if (s.Problems.Count > 0)
            {
                r.AppendLine("Problèmes relevés par les bots :");
                r.AppendLine();
                foreach (var (kind, count) in s.Problems.OrderByDescending(p => p.Value))
                    r.AppendLine($"- {kind} : {count}");
                r.AppendLine();
            }

            findings.AddRange(Findings(s).Select(f => $"**{s.Target} clients** — {f}"));
        }

        r.AppendLine("## Goulets détectés");
        r.AppendLine();
        if (findings.Count == 0)
        {
            r.AppendLine("Aucun seuil dépassé (boucle plus longue que son intervalle, écho de marche p95 > 100 ms, délai d'envoi p99 > 50 ms, " +
                         "attente de verrous > 50 ms/s, file du pool de threads > 100, CPU > 80 %, bots hors jeu, marches perdues > 1 %).");
        }
        else
        {
            foreach (var finding in findings) r.AppendLine($"- {finding}");
        }

        return r.ToString();
    }

    private static void Row(StringBuilder r, string label, Summary s) => r.AppendLine($"| {label} | {s.Count} | {s.Format()} |");

    private static void WriteServer(StringBuilder r, StageServerStats srv, StageResult s, Func<double, string, string> n)
    {
        string N(double v, string f = "0") => n(v, f);
        double Avg(List<double> l) => l.Count == 0 ? 0 : l.Average();
        double Max(List<double> l) => l.Count == 0 ? 0 : l.Max();
        List<double> Rt(string name) => srv.Runtime.GetValueOrDefault(name) ?? new List<double>();

        r.AppendLine("Serveur :");
        r.AppendLine();
        r.AppendLine($"- CPU {N(Avg(srv.CpuSamples))} % en moyenne, {N(Max(srv.CpuSamples))} % au plus (part de la machine) ; mémoire {N(Max(srv.WorkingSetMb))} Mo au plus ; {N(Max(srv.Threads))} threads au plus");
        if (srv.Runtime.Count > 0)
        {
            r.AppendLine($"- Pool de threads : {N(Max(Rt("threadpool-thread-count")))} threads au plus, file d'attente {N(Max(Rt("threadpool-queue-length")))} au plus");
            r.AppendLine($"- GC : {N(Avg(Rt("time-in-gc")), "0.0")} % du temps, {N(Rt("gen-2-gc-count").Sum())} collectes de génération 2, allocation {N(Avg(Rt("alloc-rate")) / 1048576, "0.0")} Mio/s ; exceptions {N(Rt("exception-count").Sum())}");
        }

        r.AppendLine($"- Verrous : {srv.ContentionCount} attentes, {N(srv.ContentionMs / s.Seconds, "0.0")} ms d'attente par seconde ({N(srv.ContentionMs)} ms au total)");
        r.AppendLine();

        var loops = srv.Histograms.Where(h => h.Key.StartsWith("navislamia.tick.duration")).ToList();
        if (loops.Count > 0)
        {
            r.AppendLine("| Boucle | ticks | p50 typique | p95 / p99 de la pire seconde | intervalle |");
            r.AppendLine("|---|---:|---:|---|---:|");
            foreach (var (key, h) in loops.OrderByDescending(l => l.Value.WorstP99))
            {
                var loop = Tag(key);
                r.AppendLine($"| {loop} | {h.Count} | {N(h.TypicalP50, "0.00")} | {N(h.WorstP95, "0.0")} / {N(h.WorstP99, "0.0")} | {(LoopBudgetMs.TryGetValue(loop, out var b) ? N(b) : "?")} |");
            }

            r.AppendLine();
        }

        var other = srv.Histograms.Where(h => h.Key.StartsWith("navislamia.visibility") || h.Key.StartsWith("navislamia.send")).ToList();
        if (other.Count > 0)
        {
            r.AppendLine("| Mesure (serveur, ms) | n | p50 typique | p95 / p99 de la pire seconde |");
            r.AppendLine("|---|---:|---:|---|");
            foreach (var (key, h) in other.OrderBy(o => o.Key))
                r.AppendLine($"| {Label(key)} | {h.Count} | {N(h.TypicalP50, "0.00")} | {N(h.WorstP95, "0.0")} / {N(h.WorstP99, "0.0")} |");
            r.AppendLine();
        }

        var packets = srv.Histograms.Where(h => h.Key.StartsWith("navislamia.packet")).OrderByDescending(h => h.Value.WorstP99).Take(10).ToList();
        if (packets.Count > 0)
        {
            r.AppendLine("Traitement synchrone des trames reçues, les dix plus lentes :");
            r.AppendLine();
            r.AppendLine("| Trame | n | p50 typique | p95 / p99 de la pire seconde (ms) |");
            r.AppendLine("|---|---:|---:|---|");
            foreach (var (key, h) in packets)
                r.AppendLine($"| {Tag(key)} | {h.Count} | {N(h.TypicalP50, "0.000")} | {N(h.WorstP95, "0.00")} / {N(h.WorstP99, "0.00")} |");
            r.AppendLine();
        }

        if (srv.Counters.TryGetValue("navislamia.send.bytes", out var bytes))
            r.AppendLine($"Le serveur a écrit {N(bytes / s.Seconds / 1024, "0.0")} Kio/s sur ses sockets ; {N(srv.Counters.GetValueOrDefault("navislamia.receive.frames") / s.Seconds)} trames de jeu reçues par seconde.");
        r.AppendLine();
    }

    private static string Tag(string key)
    {
        var start = key.IndexOf('[');
        if (start < 0) return key;
        var tag = key[(start + 1)..^1];
        var eq = tag.IndexOf('=');
        return eq < 0 ? tag : tag[(eq + 1)..];
    }

    private static string Label(string key) =>
        key.StartsWith("navislamia.send") ? "Délai d'envoi (file → socket)"
        : key.Contains("op=objects") ? "Visibilité des objets (PNJ, monstres, props, objets)"
        : key.Contains("op=players") ? "Visibilité entre joueurs (diffusion d'une marche)"
        : key;

    private static IEnumerable<string> Findings(StageResult s)
    {
        if (s.InWorld < s.Target) yield return $"{s.Target - s.InWorld} bot(s) hors jeu à la fin de l'étape (voir les problèmes).";
        if (s.MoveEcho.Count > 0 && s.MoveEcho.P95 > 100) yield return $"écho de marche lent : p95 {s.MoveEcho.P95:0} ms.";
        if (s.ChatEcho.Count > 0 && s.ChatEcho.P95 > 100) yield return $"écho de chat lent : p95 {s.ChatEcho.P95:0} ms.";
        if (s.WorldEntry.Count > 0 && s.WorldEntry.P95 > 2000) yield return $"entrée en jeu lente : p95 {s.WorldEntry.P95:0} ms.";
        if (s.Counters.MovesSent > 0 && s.Counters.MovesLost * 100.0 / s.Counters.MovesSent > 1)
            yield return $"{s.Counters.MovesLost} marches sur {s.Counters.MovesSent} sans écho en 5 s.";
        if (s.Server is not { } srv) yield break;

        if (srv.CpuSamples.Count > 0 && srv.CpuSamples.Max() > 80) yield return $"CPU du serveur à {srv.CpuSamples.Max():0} %.";
        if (srv.ContentionMs / s.Seconds > 50) yield return $"attente de verrous : {srv.ContentionMs / s.Seconds:0} ms par seconde.";
        if (srv.Runtime.GetValueOrDefault("threadpool-queue-length") is { Count: > 0 } queue && queue.Max() > 100)
            yield return $"file du pool de threads jusqu'à {queue.Max():0} éléments.";
        foreach (var (key, h) in srv.Histograms.Where(h => h.Key.StartsWith("navislamia.tick.duration")))
        {
            var loop = Tag(key);
            if (LoopBudgetMs.TryGetValue(loop, out var budget) && h.WorstP99 > budget)
                yield return $"la boucle « {loop} » dépasse son intervalle ({h.WorstP99:0} ms pour {budget:0} ms).";
        }

        if (srv.Histograms.FirstOrDefault(h => h.Key.StartsWith("navislamia.send")).Value is { WorstP99: > 50 } send)
            yield return $"délai d'envoi du serveur : p99 {send.WorstP99:0} ms dans la pire seconde.";
    }
}
