param(
    [string]$SqlServer = ".\SQLEXPRESS",
    [string]$Database = "Arcadia",
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\DevConsole\skill-catalog.73.json")
)

$jobIds = @(
    100, 101, 102, 103, 110, 111, 112, 113, 114, 120, 121, 122, 123, 124,
    200, 201, 202, 203, 210, 211, 212, 213, 214, 220, 221, 222, 223, 224,
    300, 301, 302, 303, 310, 311, 312, 313, 314, 320, 321, 322, 323, 324
)

$columns = 1..50 | ForEach-Object { "jp_{0:D2}" -f $_ }
$selectCosts = $columns | ForEach-Object { "jp.[$_]" }
$sql = @"
SELECT jr.id AS job_id, jr.skill_tree_id, st.skill_id,
       st.min_skill_lv, st.max_skill_lv, st.lv, st.job_lv, st.jp_ratio,
       st.need_skill_id_1, st.need_skill_lv_1,
       st.need_skill_id_2, st.need_skill_lv_2,
       st.need_skill_id_3, st.need_skill_lv_3,
       $($selectCosts -join ", ")
FROM dbo.JobResource jr
JOIN dbo.SkillTreeResource st ON st.skill_tree_id = jr.skill_tree_id
JOIN dbo.SkillJPResource jp ON jp.skill_id = st.skill_id
WHERE jr.id IN ($($jobIds -join ","))
ORDER BY jr.id, st.skill_id, st.min_skill_lv, st.max_skill_lv, st.lv, st.job_lv;
"@

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = "Server=$SqlServer;Database=$Database;Integrated Security=true;TrustServerCertificate=true"
$command = $connection.CreateCommand()
$command.CommandText = $sql
$command.CommandTimeout = 120

$jobs = [ordered]@{}
$connection.Open()
try {
    $reader = $command.ExecuteReader()
    while ($reader.Read()) {
        $jobId = $reader.GetInt32($reader.GetOrdinal("job_id"))
        $skillId = $reader.GetInt32($reader.GetOrdinal("skill_id"))
        $jobKey = [string]$jobId
        $skillKey = [string]$skillId

        if (-not $jobs.Contains($jobKey)) {
            $jobs[$jobKey] = [ordered]@{
                JobId = $jobId
                SkillTreeId = $reader.GetInt32($reader.GetOrdinal("skill_tree_id"))
                Skills = [ordered]@{}
            }
        }

        $job = $jobs[$jobKey]
        if (-not $job.Skills.Contains($skillKey)) {
            $costs = foreach ($column in $columns) {
                $reader.GetInt32($reader.GetOrdinal($column))
            }

            $job.Skills[$skillKey] = [ordered]@{
                SkillId = $skillId
                JpCosts = @($costs)
                Rules = [System.Collections.Generic.List[object]]::new()
            }
        }

        $prerequisites = [System.Collections.Generic.List[object]]::new()
        foreach ($index in 1..3) {
            $requiredSkill = $reader.GetInt32($reader.GetOrdinal("need_skill_id_$index"))
            if ($requiredSkill -ne 0) {
                $prerequisites.Add([ordered]@{
                    SkillId = $requiredSkill
                    Level = $reader.GetInt32($reader.GetOrdinal("need_skill_lv_$index"))
                })
            }
        }

        $job.Skills[$skillKey].Rules.Add([ordered]@{
            MinSkillLevel = $reader.GetInt32($reader.GetOrdinal("min_skill_lv"))
            MaxSkillLevel = $reader.GetInt32($reader.GetOrdinal("max_skill_lv"))
            RequiredLevel = $reader.GetInt32($reader.GetOrdinal("lv"))
            RequiredJobLevel = $reader.GetInt32($reader.GetOrdinal("job_lv"))
            JpRatio = [double]$reader.GetValue($reader.GetOrdinal("jp_ratio"))
            Prerequisites = @($prerequisites)
        })
    }
}
finally {
    $connection.Close()
    $connection.Dispose()
}

$catalogJobs = foreach ($job in $jobs.Values) {
    $skills = foreach ($skill in $job.Skills.Values) {
        $maxLevel = ($skill.Rules | ForEach-Object { $_["MaxSkillLevel"] } | Measure-Object -Maximum).Maximum
        $skill.JpCosts = @($skill.JpCosts | Select-Object -First ([Math]::Min(50, $maxLevel)))
        $skill
    }

    $job.Skills = @($skills)
    $job
}

$output = [ordered]@{
    Source = "Arcadia 9.4 classic-job trees and JP tables; consumed by the Epic 7.3 protocol"
    GeneratedAtUtc = [DateTime]::UtcNow.ToString("O")
    SkillCatalog = [ordered]@{ Jobs = @($catalogJobs) }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$output | ConvertTo-Json -Depth 10 -Compress | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8

$skillCount = ($catalogJobs | ForEach-Object { $_.Skills.Count } | Measure-Object -Sum).Sum
Write-Host "Exported $skillCount job/skill definitions for $($catalogJobs.Count) jobs to $resolvedOutput"
