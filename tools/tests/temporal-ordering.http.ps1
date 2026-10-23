param([string]$Api = 'http://127.0.0.1:8796')
$ErrorActionPreference = 'Stop'
if (-not ([Uri]$Api).IsLoopback) { throw 'This regression script is restricted to a local API.' }
$reports = foreach ($language in @('en', 'ro')) {
    $text = if ($language -eq 'en') {
        'Adam waited at home; years earlier, Adam had traveled to Paris; now, Adam opened the door. Margaret searched the room.'
    } else {
        'Ana a asteptat acasa; cu ani in urma, Ana a calatorit la Iasi; acum, Ana a deschis usa. Stefan a cautat cheia.'
    }
    $body = @{ text = $text; options = @{ language = $language; width = 1200; track_density = 50;
        segmentation_algorithm = 'article_rules'; progressive_density_cache = $true } } | ConvertTo-Json -Depth 8
    $response = Invoke-RestMethod "$Api/v1/diagrams" -Method Post -ContentType 'application/json; charset=utf-8' -Body $body
    if ($response.diagram.temporal_ordering.method -ne 'evidence_partial_order_v1') { throw 'Missing temporal-order metadata.' }
    $segments = @($response.diagram.segments)
    if ($segments.Count -ne 3 -or $segments[1].story_order -ge $segments[0].story_order) { throw 'Incorrect flashback placement.' }
    if (@($response.diagram.temporal_ordering.unresolved).Count -lt 1) { throw 'Unknown event order was lost.' }
    if ($response.render.svg -notmatch 'Order unresolved') { throw 'Missing SVG uncertainty marker.' }
    if (([xml]$response.xml).TYM.'TEMPORAL-ORDER'.METHOD -ne 'evidence_partial_order_v1') { throw 'Missing XML ordering metadata.' }
    $canonicalRelations = ConvertTo-Json -InputObject @($response.diagram.relations) -Depth 12 -Compress
    $views = 1
    foreach ($density in 10..100 | Where-Object { $_ % 10 -eq 0 -and $_ -ne 50 }) {
        $generated = Invoke-RestMethod "$Api/v1/density-sessions/$($response.density_cache.session_id)/$density"
        if ((ConvertTo-Json -InputObject @($generated.view.relations) -Depth 12 -Compress) -ne $canonicalRelations) { throw 'Density changed canonical temporal relations.' }
        foreach ($placement in $generated.view.segment_placements) {
            $original = $segments | Where-Object id -eq $placement.id
            if ($placement.story_order -ne $original.story_order) { throw 'Density changed temporal layout order.' }
        }
        $views++
    }
    [pscustomobject]@{ language = $language; events = @($response.diagram.events).Count; segments = $segments.Count;
        constraints = @($response.diagram.temporal_ordering.constraints).Count;
        unresolved = @($response.diagram.temporal_ordering.unresolved).Count; density_views = $views }
}
$reports | ConvertTo-Json -Depth 4
