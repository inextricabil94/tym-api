#requires -Version 7.0
<#
.SYNOPSIS
Builds publication summaries from completed, measured algorithm reports.
.DESCRIPTION
Reads six-task schema-v2 classification evidence and both completed book reports.
Rejects checkpoints or incomplete algorithm coverage. Outputs contain aggregate
numbers, SHA256 evidence hashes, and caveats; no corpus prose or absolute paths.
Fit times for classifiers are sums of three independently fitted folds.
.EXAMPLE
./tools/Tym.Benchmark/Write-AlgorithmSummary.ps1 -ClassificationReportPath private/all.json `
  -RomanianBookReportPath private/ro.json -EnglishBookReportPath private/en.json `
  -OutputDirectory docs/results/algorithm-comparison
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClassificationReportPath,
    [Parameter(Mandatory)][string]$RomanianBookReportPath,
    [Parameter(Mandatory)][string]$EnglishBookReportPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$classifierNames = @('linear_regression_ovr', 'sdca', 'lbfgs', 'decision_tree', 'fastforest_ova',
    'fasttree_ova', 'lightgbm', 'linear_svm_ova', 'knn', 'naive_bayes', 'mlp', 'cnn', 'rnn', 'transformer')
$taskNames = @('timebank_event_class', 'timebank_event_tense', 'timebank_timex_type',
    'timebank_tlink', 'timebank_slink', 'timebank_alink')
$bookNames = @('mlnet_kmeans', 'mlnet_pca_kmeans', 'csharp_average_linkage_hierarchical',
    'csharp_dbscan', 'csharp_torchsharp_autoencoder')
$variantLabels = @{
    linear_regression_ovr = 'Linear regression OVR'; sdca = 'Logistic regression SDCA'; lbfgs = 'Logistic regression L-BFGS'
    decision_tree = 'Decision tree CART'; fastforest_ova = 'Random forest OVA'; fasttree_ova = 'Gradient boosting FastTree OVA'
    lightgbm = 'Gradient boosting LightGBM'; linear_svm_ova = 'Linear SVM OVA'; knn = 'KNN'; naive_bayes = 'Naive Bayes'
    mlp = 'Neural network MLP'; cnn = 'Text CNN'; rnn = 'LSTM'; transformer = 'Scratch Transformer'
    mlnet_kmeans = 'KMeans'; mlnet_pca_kmeans = 'PCA + KMeans'; csharp_average_linkage_hierarchical = 'Average-linkage hierarchical'
    csharp_dbscan = 'DBSCAN'; csharp_torchsharp_autoencoder = 'Numeric autoencoder'
}
$families = @(
    [ordered]@{ id = 'linear_regression'; label = 'Linear regression'; variants = @('linear_regression_ovr'); kind = 'classification' },
    [ordered]@{ id = 'logistic_regression'; label = 'Logistic regression'; variants = @('sdca', 'lbfgs'); kind = 'classification' },
    [ordered]@{ id = 'decision_tree'; label = 'Decision tree'; variants = @('decision_tree'); kind = 'classification' },
    [ordered]@{ id = 'random_forest'; label = 'Random forest'; variants = @('fastforest_ova'); kind = 'classification' },
    [ordered]@{ id = 'gradient_boosting'; label = 'Gradient boosting'; variants = @('fasttree_ova', 'lightgbm'); kind = 'classification' },
    [ordered]@{ id = 'svm'; label = 'SVM'; variants = @('linear_svm_ova'); kind = 'classification' },
    [ordered]@{ id = 'knn'; label = 'KNN'; variants = @('knn'); kind = 'classification' },
    [ordered]@{ id = 'naive_bayes'; label = 'Naive Bayes'; variants = @('naive_bayes'); kind = 'classification' },
    [ordered]@{ id = 'kmeans'; label = 'KMeans'; variants = @('mlnet_kmeans'); kind = 'unlabeled_exploration' },
    [ordered]@{ id = 'hierarchical_clustering'; label = 'Hierarchical clustering'; variants = @('csharp_average_linkage_hierarchical'); kind = 'unlabeled_exploration' },
    [ordered]@{ id = 'pca'; label = 'PCA'; variants = @('mlnet_pca_kmeans'); kind = 'unlabeled_exploration' },
    [ordered]@{ id = 'mlp'; label = 'Neural network MLP'; variants = @('mlp'); kind = 'classification' },
    [ordered]@{ id = 'cnn'; label = 'CNN'; variants = @('cnn'); kind = 'classification' },
    [ordered]@{ id = 'rnn'; label = 'RNN'; variants = @('rnn'); kind = 'classification' },
    [ordered]@{ id = 'transformer'; label = 'Transformer'; variants = @('transformer'); kind = 'classification' },
    [ordered]@{ id = 'autoencoder'; label = 'Autoencoder'; variants = @('csharp_torchsharp_autoencoder'); kind = 'unlabeled_exploration' },
    [ordered]@{ id = 'dbscan'; label = 'DBSCAN'; variants = @('csharp_dbscan'); kind = 'unlabeled_exploration' }
)

function Read-Report([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Path))
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF)
    return ($text | ConvertFrom-Json -Depth 100)
}
function Value([object]$Object, [string]$Name) {
    if ($null -eq $Object) { throw "Missing object for required field '$Name'." }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Missing required aggregate field '$Name'." }
    return $property.Value
}
function Number([object]$Object, [string]$Name, [double]$Minimum = 0, [double]$Maximum = [double]::MaxValue) {
    $value = Value $Object $Name
    if ($null -eq $value -or $value -is [bool] -or $value -is [string]) { throw "Field '$Name' must be a measured number." }
    $number = [double]$value
    if (-not [double]::IsFinite($number) -or $number -lt $Minimum -or $number -gt $Maximum) {
        throw "Field '$Name' is nonfinite or outside its allowed range."
    }
    return $number
}
function Optional-Number([object]$Object, [string]$Name, [double]$Minimum = 0, [double]$Maximum = [double]::MaxValue) {
    if ($null -eq (Value $Object $Name)) { return $null }
    return (Number $Object $Name $Minimum $Maximum)
}
function Require-Set([string[]]$Actual, [string[]]$Expected, [string]$Description) {
    if ($Actual.Count -ne $Expected.Count -or @($Actual | Sort-Object -Unique).Count -ne $Actual.Count -or
        @($Actual | Where-Object { $_ -notin $Expected }).Count -ne 0) {
        throw "Incomplete or unexpected $Description. This summary requires all declared measured variants."
    }
}
function Format-Number([object]$Number, [int]$Digits = 3) {
    if ($null -eq $Number) { return 'N/A' }
    return ([double]$Number).ToString("F$Digits", $culture)
}
function Classifier-Space([object[]]$Folds, [string]$Config) {
    $trials = @($Folds | ForEach-Object { Value (Value $_ 'algorithms') $Config })
    $neural = $Config -in @('mlp', 'cnn', 'rnn', 'transformer')
    $dimensions = if ($neural) { @() } else { @($trials | ForEach-Object { Number $_ 'feature_dimensions' 1 }) }
    $parameters = if ($neural) { @($trials | ForEach-Object { Number (Value $_ 'configuration') 'model_parameters' 1 }) } else { @() }
    $vocabulary = if ($neural) { @($trials | ForEach-Object { Number (Value $_ 'configuration') 'training_vocabulary_size' 2 2048 }) } else { @() }
    return [pscustomobject][ordered]@{
        feature_dimensions_min = if ($neural) { $null } else { ($dimensions | Measure-Object -Minimum).Minimum }
        feature_dimensions_max = if ($neural) { $null } else { ($dimensions | Measure-Object -Maximum).Maximum }
        training_vocabulary_size_min = if ($neural) { ($vocabulary | Measure-Object -Minimum).Minimum } else { $null }
        training_vocabulary_size_max = if ($neural) { ($vocabulary | Measure-Object -Maximum).Maximum } else { $null }
        maximum_sequence_length = if ($neural) { 64 } else { $null }
        process_working_set_before_min_mb = ($trials | ForEach-Object { Number $_ 'process_working_set_before_mb' } | Measure-Object -Minimum).Minimum
        process_working_set_after_max_mb = ($trials | ForEach-Object { Number $_ 'process_working_set_after_mb' } | Measure-Object -Maximum).Maximum
        process_lifetime_peak_working_set_max_mb = ($trials | ForEach-Object { Number $_ 'process_peak_working_set_mb' } | Measure-Object -Maximum).Maximum
        neural_parameter_count_min = if ($neural) { ($parameters | Measure-Object -Minimum).Minimum } else { $null }
        neural_parameter_count_max = if ($neural) { ($parameters | Measure-Object -Maximum).Maximum } else { $null }
        float32_parameter_bytes_max = if ($neural) { 4 * ($parameters | Measure-Object -Maximum).Maximum } else { $null }
        artifact_bytes = $null
        scope = 'Min/max over three folds. Working set is the shared process snapshot before/after each trial; lifetime peak includes earlier algorithms, native state and retained data. Not isolated model RAM. Feature dimensions are the fitted concatenated input, before CART selection. Float32 parameter bytes exclude gradients, optimizer, activations and vocabulary. Classifier weights were not exported; disk size is unavailable.'
    }
}
function Book-Space([object]$Algorithm) {
    $space = Value $Algorithm 'space'
    $dimensions = Number $Algorithm 'feature_dimensions' 2
    $parameters = Optional-Number $space 'neural_parameter_count' 1
    $bytes = Optional-Number $space 'float32_parameter_bytes' 4
    if ($null -ne $parameters -and $bytes -ne 4 * $parameters) { throw 'Parameter storage must match float32 parameter count.' }
    return [pscustomobject][ordered]@{
        feature_dimensions_min = $dimensions; feature_dimensions_max = $dimensions
        training_vocabulary_size_min = $null; training_vocabulary_size_max = $null; maximum_sequence_length = $null
        process_working_set_before_min_mb = $null
        process_working_set_after_max_mb = Number $space 'process_working_set_mb'
        process_lifetime_peak_working_set_max_mb = Number $space 'process_lifetime_peak_working_set_mb'
        neural_parameter_count_min = $parameters; neural_parameter_count_max = $parameters
        float32_parameter_bytes_max = $bytes
        artifact_bytes = Optional-Number $space 'artifact_bytes' 1
        scope = [string](Value $space 'scope')
    }
}
function Add-SpaceTable([Text.StringBuilder]$Builder, [object[]]$Measurements) {
    [void]$Builder.AppendLine('| Task/language | Variant | Feature dimension range | Vocabulary range | Parameters max | Float32 parameter MiB | Shared WS after max MiB | Process lifetime peak max MiB | Private artifact bytes |')
    [void]$Builder.AppendLine('| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
    foreach ($row in $Measurements) {
        $s = $row.space
        $parameterMiB = if ($null -eq $s.float32_parameter_bytes_max) { $null } else { $s.float32_parameter_bytes_max / 1048576.0 }
        [void]$Builder.AppendLine('| ' + $row.task + '/' + $row.language + ' | ' + $variantLabels[$row.config] +
            ' | ' + (Format-Number $s.feature_dimensions_min 0) + '..' + (Format-Number $s.feature_dimensions_max 0) +
            ' | ' + (Format-Number $s.training_vocabulary_size_min 0) + '..' + (Format-Number $s.training_vocabulary_size_max 0) +
            ' | ' + (Format-Number $s.neural_parameter_count_max 0) + ' | ' + (Format-Number $parameterMiB 4) +
            ' | ' + (Format-Number $s.process_working_set_after_max_mb 2) + ' | ' + (Format-Number $s.process_lifetime_peak_working_set_max_mb 2) +
            ' | ' + (Format-Number $s.artifact_bytes 0) + ' |')
    }
}
function Metric-Text([object]$Measurement) {
    switch ($Measurement.config) {
        { $_ -in @('mlnet_kmeans', 'mlnet_pca_kmeans') } {
            $projection = if ($Measurement.config -eq 'mlnet_pca_kmeans') {
                '; PCA basis fit s=' + (Format-Number $Measurement.pca_basis_fit_seconds 3)
            } else { '' }
            return ('heldout distance=' + (Format-Number $Measurement.average_squared_centroid_distance 5) +
                '; DBI=' + (Format-Number $Measurement.davies_bouldin_index 4) + $projection)
        }
        'csharp_torchsharp_autoencoder' {
            return ('heldout MSE=' + (Format-Number $Measurement.reconstruction_mse 6) +
                '; mean baseline=' + (Format-Number $Measurement.training_mean_baseline_mse 6) +
                '; zero baseline=' + (Format-Number $Measurement.zero_baseline_mse 6))
        }
        default {
            return ('sample clusters=' + $Measurement.observed_clusters + '; noise=' +
                (Format-Number ($Measurement.noise_fraction * 100) 2) + '%; silhouette=' +
                (Format-Number $Measurement.silhouette_excluding_noise 4))
        }
    }
}
function Add-ClassifierTable([Text.StringBuilder]$Builder, [object[]]$Measurements) {
    [void]$Builder.AppendLine('| Variant | Accuracy % | Macro F1 % | Sum 3-fold fit s | Sum prediction s | Batch ms/row | Batch rows/s |')
    [void]$Builder.AppendLine('| --- | ---: | ---: | ---: | ---: | ---: | ---: |')
    foreach ($row in $Measurements) {
        [void]$Builder.AppendLine('| ' + $variantLabels[$row.config] + ' | ' + (Format-Number $row.accuracy_percent 2) +
            ' | ' + (Format-Number $row.macro_f1_percent 2) + ' | ' + (Format-Number $row.train_seconds 3) +
            ' | ' + (Format-Number $row.predict_seconds 3) + ' | ' + (Format-Number $row.batched_ms_per_row 5) +
            ' | ' + (Format-Number $row.batched_rows_per_second 2) + ' |')
    }
}

$classification = Read-Report $ClassificationReportPath
$runtime = Value $classification 'runtime'
if ((Value $classification 'schema_version') -ne 2 -or
    (Value $classification 'status') -ne 'diagnostic_provided_annotation_cross_validation' -or
    (Number $classification 'folds' 3 3) -ne 3) {
    throw 'Classification evidence must be a completed schema-v2 three-fold report; training checkpoints are rejected.'
}
Require-Set @(Value $classification 'algorithms') $classifierNames 'classifier configuration list'
$ngramLimit = [int](Number $classification 'native_maximum_ngrams_per_order_per_channel' 1024 1024)
$neuralEpochs = [int](Number $classification 'neural_epochs' 5 5)
$tasks = @(Value $classification 'tasks')
Require-Set @($tasks | ForEach-Object { Value $_ 'task' }) $taskNames 'six-task classification evidence'
$classificationRows = [Collections.Generic.List[object]]::new()
$baselines = [Collections.Generic.List[object]]::new()
foreach ($taskName in $taskNames) {
    $task = @($tasks | Where-Object { (Value $_ 'task') -eq $taskName })[0]
    if ((Value $task 'language') -ne 'ro') { throw 'All six classification tasks must be Romanian.' }
    $rows = Number $task 'rows' 1
    $metrics = Value $task 'algorithms'; $performance = Value $task 'performance'
    Require-Set @($metrics.PSObject.Properties.Name) @($classifierNames + 'majority') 'pooled classifier metrics'
    Require-Set @($performance.PSObject.Properties.Name) @($classifierNames + 'majority') 'classifier timing measurements'
    $foldReports = @(Value $task 'folds')
    if ($foldReports.Count -ne 3 -or @($foldReports | ForEach-Object { Number $_ 'fold' 0 2 } | Sort-Object -Unique).Count -ne 3) {
        throw 'Each task must contain exactly three completed fold reports.'
    }
    if (($foldReports | ForEach-Object { Number $_ 'validation_rows' 1 } | Measure-Object -Sum).Sum -ne $rows) {
        throw 'Fold validation row counts do not match pooled task rows.'
    }
    foreach ($fold in $foldReports) {
        Require-Set @((Value $fold 'algorithms').PSObject.Properties.Name) @($classifierNames + 'majority') 'completed fold variants'
        foreach ($config in $classifierNames) {
            $trial = Value (Value $fold 'algorithms') $config
            $configuration = Value $trial 'configuration'
            if ($config -in @('mlp', 'cnn', 'rnn', 'transformer')) {
                $metadata = Value $configuration 'metadata'
                [void](Number $metadata 'maximum_vocabulary_size_including_pad_and_unknown' 2048 2048)
                [void](Number $metadata 'maximum_sequence_length' 64 64)
                [void](Number $metadata 'epochs' $neuralEpochs $neuralEpochs)
                if ((Value $metadata 'pretrained_weights') -ne $false) { throw 'Expected scratch-trained neural evidence.' }
            } else {
                $featurizer = Value $configuration 'featurizer'
                [void](Number $featurizer 'maximum_ngrams_per_order_per_channel' $ngramLimit $ngramLimit)
                $channelCount = @(Value $trial 'feature_channels').Count
                if ($channelCount -lt 1) { throw 'Native/numeric text models require active training feature channels.' }
                $bound = 3 * $ngramLimit * $channelCount
                [void](Number $featurizer 'maximum_concatenated_features' $bound $bound)
                [void](Number $trial 'feature_dimensions' 1 $bound)
            }
        }
    }
    foreach ($config in @($classifierNames + 'majority')) {
        $metric = Value $metrics $config; $cost = Value $performance $config
        if ((Number $metric 'rows' 1) -ne $rows -or (Number $cost 'prediction_rows' 1) -ne $rows) {
            throw 'Prediction counts must match the full out-of-fold task row count.'
        }
        $accuracy = Number $metric 'accuracy' 0 1; $macro = Number $metric 'macro_f1' 0 1
        $measurement = [pscustomobject][ordered]@{
            config = $config; task = $taskName; language = 'ro'; status = 'completed_provided_annotation_diagnostic'
            rows = [int]$rows; fold_count = 3; train_seconds = Number $cost 'total_training_seconds'
            predict_seconds = Number $cost 'total_prediction_seconds'
            batched_ms_per_row = Number $cost 'prediction_ms_per_row'
            batched_rows_per_second = Number $cost 'batched_rows_per_second'
            accuracy_fraction = $accuracy; macro_f1_fraction = $macro
            accuracy_percent = $accuracy * 100; macro_f1_percent = $macro * 100
            space = if ($config -eq 'majority') { $null } else { Classifier-Space $foldReports $config }
            notes = 'Provided annotation, adjudication unknown; pooled out-of-fold conditional classification; fit is SUM of 3 independently fitted folds; batched timing is not interactive latency.'
        }
        if ($config -eq 'majority') { $baselines.Add($measurement) } else { $classificationRows.Add($measurement) }
    }
}

$bookRows = [Collections.Generic.List[object]]::new()
$bookSummaries = [Collections.Generic.List[object]]::new()
$bookInputs = @(@{ Language = 'ro'; Path = $RomanianBookReportPath }, @{ Language = 'en'; Path = $EnglishBookReportPath })
foreach ($item in $bookInputs) {
    $book = Read-Report $item.Path
    if ((Value $book 'schema_version') -ne 1 -or (Value $book 'status') -ne 'exploratory_unlabeled_books' -or
        (Value $book 'language') -ne $item.Language) { throw 'Expected completed unlabeled book evidence in the specified language.' }
    $total = Number $book 'total_rows' 1; $train = Number $book 'training_rows' 1; $heldout = Number $book 'heldout_rows' 1
    if ($train + $heldout -ne $total) { throw 'Book training and heldout counts do not cover the input.' }
    $split = [string](Value $book 'split_kind')
    if ($split -notin @('complete_document_component_holdout', 'within_source_work_normalized_passage_holdout')) {
        throw 'Unknown book split protocol.'
    }
    $bookAlgorithms = @(Value $book 'algorithms')
    Require-Set @($bookAlgorithms | ForEach-Object { Value $_ 'algorithm' }) $bookNames 'five measured book algorithms'
    $bookSummaries.Add([pscustomobject][ordered]@{
        language = $item.Language; rows = [int]$total; training_rows = [int]$train; heldout_rows = [int]$heldout
        documents = [int](Number $book 'documents' 1); split_kind = $split
        independent_document_components = [int](Number $book 'independent_document_components' 1)
        input_sha256 = Value $book 'input_sha256'; report_sha256 = (Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    })
    foreach ($config in $bookNames) {
        $algorithm = @($bookAlgorithms | Where-Object { (Value $_ 'algorithm') -eq $config })[0]
        if ($null -ne (Value $algorithm 'semantic_accuracy')) { throw 'Unlabeled book evidence must not claim semantic accuracy.' }
        $measurement = [ordered]@{
            config = $config; task = 'unlabeled_book_exploration'; language = $item.Language; status = 'completed_unlabeled_exploration'
            split_kind = $split; training_rows = [int]$train; heldout_rows = [int]$heldout
            feature_dimensions = [int](Number $algorithm 'feature_dimensions' 2)
            train_seconds = $null; predict_seconds = $null; batched_ms_per_row = $null; batched_rows_per_second = $null
            accuracy_fraction = $null; macro_f1_fraction = $null; sample_rows = $null; pca_basis_fit_seconds = $null; timing_scope = ''; notes = ''
            space = Book-Space $algorithm
        }
        switch ($config) {
            { $_ -in @('mlnet_kmeans', 'mlnet_pca_kmeans') } {
                $assessment = Value $algorithm 'heldout_metrics'
                if ((Number $assessment 'rows' 1) -ne $heldout -or
                    (Number (Value $algorithm 'training_metrics') 'rows' 1) -ne $train) { throw 'KMeans assessment counts do not match the shared book split.' }
                $measurement.train_seconds = Number $algorithm 'fit_seconds_including_shared_featurizer'
                $measurement.predict_seconds = Number $algorithm 'assessment_seconds'
                $measurement.average_squared_centroid_distance = Optional-Number $assessment 'average_squared_centroid_distance'
                $measurement.davies_bouldin_index = Optional-Number $assessment 'davies_bouldin_index'
                $measurement.observed_clusters = @((Value $assessment 'cluster_counts')).Count
                if ($config -eq 'mlnet_pca_kmeans') {
                    $autoencoderEvidence = @($bookAlgorithms | Where-Object { (Value $_ 'algorithm') -eq 'csharp_torchsharp_autoencoder' })[0]
                    $measurement.pca_basis_fit_seconds = Number $autoencoderEvidence 'shared_pca_fit_seconds'
                }
                $measurement.timing_scope = 'Fit includes shared text featurizer; assessment transforms/scores BOTH training and heldout rows and computes cluster summaries.'
                $measurement.notes = 'Heldout lexical geometry; distance/DBI across text and PCA feature spaces are not directly comparable semantic measures.'
            }
            'csharp_torchsharp_autoencoder' {
                if ((Number $algorithm 'training_rows' 1) -ne $train -or (Number $algorithm 'heldout_rows' 1) -ne $heldout) {
                    throw 'Autoencoder counts do not match the shared book split.'
                }
                if ((Value $algorithm 'model_exported') -ne $false) { throw 'Unexpected exported-autoencoder contract.' }
                $measurement.train_seconds = Number $algorithm 'fit_seconds_including_shared_representation'
                $measurement.predict_seconds = Number $algorithm 'prediction_seconds_including_heldout_transform'
                $measurement.reconstruction_mse = Number $algorithm 'heldout_reconstruction_mean_squared_error'
                $measurement.training_mean_baseline_mse = Number $algorithm 'heldout_training_mean_reconstruction_mean_squared_error'
                $measurement.zero_baseline_mse = Number $algorithm 'heldout_zero_reconstruction_mean_squared_error'
                $measurement.bottleneck_dimensions = [int](Number $algorithm 'bottleneck_dimensions' 1)
                $measurement.model_parameters = [long](Number $algorithm 'model_parameters' 1)
                $measurement.timing_scope = 'Fit includes shared text/PCA fitting and training-vector transformation; prediction includes heldout transformation, reconstruction/MSE and latent encoding.'
                $measurement.notes = 'Scratch C# TorchSharp neural experiment; normalized PCA-vector reconstruction, not prose generation or semantic accuracy; weights not exported/deployed.'
            }
            default {
                $sampleMetrics = Value $algorithm 'sample_metrics'
                $sampleRows = Number $algorithm 'sample_rows' 1 256
                if ((Number $sampleMetrics 'rows' 1 256) -ne $sampleRows -or $sampleRows -gt $train) {
                    throw 'Custom clustering sample must be bounded and training-only.'
                }
                if ($null -ne (Value $algorithm 'heldout_metrics')) { throw 'Transductive sample clustering must not claim heldout scoring.' }
                $measurement.sample_rows = [int]$sampleRows
                $measurement.train_seconds = (Number $algorithm 'shared_text_featurizer_fit_seconds') + (Number $algorithm 'shared_pca_fit_seconds') +
                    (Number $algorithm 'sample_feature_transform_seconds') + (Number $algorithm 'fit_seconds_algorithm_only')
                $measurement.predict_seconds = Number $algorithm 'assessment_seconds'
                $measurement.observed_clusters = [int](Number $sampleMetrics 'clusters')
                $measurement.noise_fraction = Number $sampleMetrics 'noise_fraction' 0 1
                $measurement.silhouette_excluding_noise = Optional-Number $sampleMetrics 'silhouette_excluding_noise' -1 1
                $measurement.timing_scope = 'Fit includes shared text/PCA fitting, bounded training-sample projection and clustering; assessment is sample silhouette/noise, not heldout prediction.'
                $measurement.notes = 'Transductive deterministic training sample only; no supervised or heldout classification accuracy.'
            }
        }
        $bookRows.Add([pscustomobject]$measurement)
    }
}

$familyRows = @($families | ForEach-Object {
    $family = $_
    $measurements = if ($family.kind -eq 'classification') {
        @($classificationRows | Where-Object { $_.config -in $family.variants })
    } else { @($bookRows | Where-Object { $_.config -in $family.variants }) }
    [pscustomobject][ordered]@{
        id = $family.id; label = $family.label; variants = $family.variants; kind = $family.kind
        status = 'measured_completed'; measurement_count = $measurements.Count
    }
})
$familyByVariant = @{}
foreach ($family in $familyRows) { foreach ($variant in $family.variants) { $familyByVariant[$variant] = $family } }
$classificationFlat = @($classificationRows | ForEach-Object {
    $row = $_; $family = $familyByVariant[$row.config]
    $majority = @($baselines | Where-Object { $_.task -eq $row.task })[0]
    $majorityNote = 'Training-fold majority baseline for this task: accuracy ' + (Format-Number $majority.accuracy_percent 2) +
        '%; full-inventory macro F1 ' + (Format-Number $majority.macro_f1_percent 2) + '%.'
    $featureNote = if ($row.config -in @('mlp', 'cnn', 'rnn', 'transformer')) {
        'Scratch TorchSharp model: training-only vocabulary capped at 2048 including padding/unknown; first 64 tokens per input; fixed 5 epochs.'
    } else {
        'Training-only ML.NET feature dictionaries: at most 1024 word unigrams, 1024 word bigrams and 1024 character trigrams per active channel; total dimension <= 3 x 1024 x active channels. CART further selects bounded training features; exact KNN uses the complete capped vectors.'
    }
    [pscustomobject][ordered]@{
        family = $family.id; family_label = $family.label; algorithm = $row.config; algorithm_label = $variantLabels[$row.config]
        task = $row.task; language = $row.language; status = $row.status; rows = $row.rows
        accuracy_fraction = $row.accuracy_fraction; macro_f1_fraction = $row.macro_f1_fraction
        total_training_seconds = $row.train_seconds; total_prediction_seconds = $row.predict_seconds
        prediction_ms_per_row = $row.batched_ms_per_row; batched_rows_per_second = $row.batched_rows_per_second
        space = $row.space
        notes = @($row.notes, $featureNote, $majorityNote)
    }
})
$booksFlat = @($bookRows | ForEach-Object {
    $row = $_; $family = $familyByVariant[$row.config]
    $metrics = switch ($row.config) {
        { $_ -in @('mlnet_kmeans', 'mlnet_pca_kmeans') } {
            [ordered]@{ heldout_average_squared_centroid_distance = $row.average_squared_centroid_distance
                heldout_davies_bouldin_index = $row.davies_bouldin_index; observed_heldout_clusters = $row.observed_clusters
                pca_basis_fit_seconds = $row.pca_basis_fit_seconds }
        }
        'csharp_torchsharp_autoencoder' {
            [ordered]@{ heldout_reconstruction_mse = $row.reconstruction_mse; heldout_training_mean_baseline_mse = $row.training_mean_baseline_mse
                heldout_zero_baseline_mse = $row.zero_baseline_mse; input_dimensions = $row.feature_dimensions
                bottleneck_dimensions = $row.bottleneck_dimensions; model_parameters = $row.model_parameters }
        }
        default {
            [ordered]@{ sample_rows = $row.sample_rows; observed_sample_clusters = $row.observed_clusters
                sample_noise_fraction = $row.noise_fraction; sample_silhouette_excluding_noise = $row.silhouette_excluding_noise }
        }
    }
    $scope = if ($null -ne $row.sample_rows) { 'training-only transductive sample n=' + $row.sample_rows + '; ' + $row.split_kind }
        else { 'heldout n=' + $row.heldout_rows + '; ' + $row.split_kind }
    [pscustomobject][ordered]@{
        family = $family.id; family_label = $family.label; algorithm = $row.config; algorithm_label = $variantLabels[$row.config]
        language = $row.language; status = $row.status; training_seconds = $row.train_seconds; assessment_seconds = $row.predict_seconds
        semantic_accuracy = $null; metrics = [pscustomobject]$metrics; scope = $scope
        space = $row.space
        notes = @($row.notes, $row.timing_scope)
    }
})
$limits = @(
    'Seventeen chart families, nineteen distinct implemented variants: fourteen classifiers from twelve families plus five unlabeled exploration pipelines. PCA is measured as PCA+KMeans, not as a semantic classifier.',
    'Classification accuracy and macro F1 are pooled out-of-fold metrics on supplied Romanian annotations of unknown adjudication, conditional on supplied mentions/endpoints. They do not measure extraction or complete graph accuracy.',
    'Macro F1 uses the fixed full task label inventory, retaining rare labels absent from some training folds with zero F1 when unrecovered. Training-fold majority baselines are retained for each task.',
    'Each classification fit time is the SUM of three separately fitted folds, not one final production-model fit. Feature fitting/training vectorization is included; CSV accuracy_fraction and macro_f1_fraction use [0,1], Markdown displays percentages.',
    'Prediction costs are batched validation measurements and include vectorization. They are not interactive latency. One local CPU run supplies no timing confidence interval or hardware-normalized speed ranking.',
    'Space measurements use MiB (1024 squared bytes). Working sets and lifetime peaks belong to the shared process; earlier algorithms, retained data, garbage collection and native state affect them. They are not isolated per-model RAM and must not be used to rank model memory. Before/after differences are not allocation measurements.',
    'Neural parameter bytes are actual parameter counts times four for float32, excluding gradients, optimizer state, activations, vocabulary and upstream representation. Classification weights and autoencoder weights were not exported: disk size is N/A. Book artifact sizes describe compressed ML.NET pipelines or private transductive sample JSON, not comparable deployment models; no corpus prose is published.',
    'One thread is requested in trainer options where exposed, not enforced by process affinity. Framework/native auxiliary workers and operating-system load are not isolated; one-versus-all scoring can parallelize class mappers. Hardware and requested thread settings accompany this report.',
    'All supervised variants use the same document-component splits, but native, bounded custom and scratch-neural feature representations differ. No independent algorithm-selection holdout or significance test is available.',
    'Classification native/numeric text features use training-only dictionaries capped at 1024 word unigrams, 1024 word bigrams and 1024 character trigrams per active channel: at most 3 x 1024 x active channels. Blank channels are removed using training input only. Actual dimensions may be smaller; CART further selects bounded training features, while exact KNN uses the complete capped vectors.',
    'Scratch neural classifiers use a separate training-only vocabulary capped at 2048 tokens including padding/unknown, the first 64 tokens per input and five fixed epochs. These bounded representations differ from the native n-gram channels.',
    'A runtime-only uncapped pilot was stopped before accuracy inspection because sparse tree fits were expensive. The shared n-gram budget is a fixed resource decision for this completed comparison, not a validation-score optimization.',
    'MLP, CNN, LSTM, Transformer and autoencoder are scratch TorchSharp C# experiments alongside ML.NET, not pretrained BERT/GPT or a reproduction of the historical paper.',
    'Linear regression is adapted with separate 0/1 class-indicator targets and argmax selection. It does not regress ordinal label numbers or calendar dates. SVM uses linear margins; the comparison does not cover every variant of each chart family.',
    'Books have no supervised labels: accuracy and F1 are unavailable. The English within-work passage holdout is not evidence of cross-book generalization.',
    'KMeans assessment times include both training and heldout cluster summaries. Hierarchical/DBSCAN times describe a bounded training-only sample. Autoencoder prediction time describes heldout numeric reconstruction. These workloads are not directly comparable.',
    'Book fit figures include the shared text/PCA prerequisites where applicable. Do not sum these per-algorithm figures to estimate total run time, because shared costs are repeated in the presentation.',
    'Distances and Davies-Bouldin values from different feature spaces are not directly comparable semantic measures. Autoencoder MSE reconstructs normalized PCA vectors and is compared with training-mean and zero-vector baselines.',
    'Current production classifiers remain separate from these exploratory comparisons; comparison runs do not promote or deploy alternative weights.'
)
$release = [pscustomobject][ordered]@{
    schema_version = 1; status = 'completed_algorithm_comparison'; chart_family_count = 17; unique_variant_count = 19
    classification_variant_count = 14; classification_family_count = 12; classification_task_count = 6
    book_variant_count = 5; book_languages = @('ro', 'en')
    coverage = [ordered]@{ families = 17; unique_variants = 19; classification_variants = 14; classification_families = 12; classification_tasks = 6; book_variants = 5 }
    classification_primary_task = 'timebank_tlink'; classification_rows = $classificationFlat; books_rows = $booksFlat
    classification_completed_utc = Value $classification 'completed_utc'
    classification_report_sha256 = (Get-FileHash -LiteralPath $ClassificationReportPath -Algorithm SHA256).Hash.ToLowerInvariant()
    classification_input_sha256 = Value $classification 'input_sha256'
    classification_feature_representation = Value $classification 'feature_representation'
    classification_elapsed_seconds = Number $classification 'seconds'
    classification_runtime = [ordered]@{
        os = Value $runtime 'os'; architecture = Value $runtime 'architecture'
        logical_processors = Number $runtime 'logical_processors' 1; dotnet = Value $runtime 'dotnet'
        configured_trainer_threads_where_exposed = Number $runtime 'trainer_threads' 1 1
        process_affinity_or_auxiliary_worker_limit_enforced = $false
        scope = 'Per-trainer requested options; OVA scoring may parallelize classes; native/framework workers and OS load are not isolated.'
    }
    classification_feature_contract = [ordered]@{
        native_maximum_ngrams_per_order_per_channel = $ngramLimit
        native_active_word_ngram_lengths = @(1, 2); native_active_character_ngram_lengths = @(3)
        native_maximum_features_per_active_channel = 3 * $ngramLimit
        native_total_dimension_bound = '3 x 1024 x number of active training channels'
        feature_dictionary_fit = 'training fold only'
        neural_vocabulary_limit_including_padding_unknown = 2048; neural_sequence_token_limit = 64; neural_fixed_epochs = $neuralEpochs
        neural_pretrained_weights = $false
    }
    seed = Value $classification 'seed'; folds = 3
    units = [ordered]@{ accuracy = 'fraction [0,1]; Markdown and UI display percentages'; macro_f1 = 'fraction [0,1]; Markdown and UI display percentages'; time = 'seconds'; batched_latency = 'milliseconds per validation row, not interactive latency'; process_memory_mb_fields = 'MiB, bytes / 1048576, shared process snapshots'; artifact_and_parameter_size = 'bytes' }
    books = $bookSummaries.ToArray(); families = $familyRows; majority_baselines = $baselines.ToArray(); limitations = $limits
}

$markdown = [Text.StringBuilder]::new()
[void]$markdown.AppendLine('# Algorithm comparison')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('Measured coverage: **17 chart families / 19 variants**: 14 classification variants from 12 families, plus five unlabeled book pipelines in Romanian and English.')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('Classification uses the same three document-component folds. Fit seconds are the **sum of three independently fitted models**, not a single final fit. Accuracy and macro F1 are diagnostic conditional label scores on supplied annotations with unknown adjudication.')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('Feature budgets are fixed before this run: native/numeric models use train-only word unigrams/bigrams and character trigrams, each capped at 1024 per channel (dimension <= 3 x 1024 x active channels). Scratch neural models use a train-only vocabulary <= 2048 including padding/unknown, first 64 tokens, five epochs. This is a resource-bounded comparison with different representations.')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('## Temporal links: all classification variants')
[void]$markdown.AppendLine()
Add-ClassifierTable $markdown @($classificationRows | Where-Object { $_.task -eq 'timebank_tlink' })
[void]$markdown.AppendLine()
$primaryMajority = @($baselines | Where-Object { $_.task -eq 'timebank_tlink' })[0]
[void]$markdown.AppendLine('TLINK training-fold majority baseline: accuracy ' + (Format-Number $primaryMajority.accuracy_percent 2) +
    '%; full-inventory macro F1 ' + (Format-Number $primaryMajority.macro_f1_percent 2) + '%.')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('## Complete six-task measurements')
foreach ($taskName in $taskNames) {
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('### ' + $taskName)
    [void]$markdown.AppendLine()
    $selected = @($classificationRows | Where-Object { $_.task -eq $taskName })
    [void]$markdown.AppendLine('Rows: ' + $selected[0].rows + '. Language: Romanian. Every row is scored once out of fold.')
    [void]$markdown.AppendLine()
    Add-ClassifierTable $markdown $selected
    $baseline = @($baselines | Where-Object { $_.task -eq $taskName })[0]
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('Training-fold majority baseline: accuracy ' + (Format-Number $baseline.accuracy_percent 2) + '%; macro F1 ' + (Format-Number $baseline.macro_f1_percent 2) + '%.')
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('## Books: unlabeled exploration')
[void]$markdown.AppendLine()
foreach ($book in $bookSummaries) {
    [void]$markdown.AppendLine('- ' + $book.language.ToUpperInvariant() + ': ' + $book.rows + ' passages, ' + $book.documents + ' documents, ' +
        $book.training_rows + ' training / ' + $book.heldout_rows + ' heldout; `' + $book.split_kind + '`.')
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('| Language | Pipeline | Fit s, including prerequisites | Prediction/assessment s | Accuracy | Metrics | Scope |')
[void]$markdown.AppendLine('| --- | --- | ---: | ---: | --- | --- | --- |')
foreach ($row in $bookRows) {
    $scope = if ($null -ne $row.sample_rows) { 'training sample n=' + $row.sample_rows } else { 'shared holdout n=' + $row.heldout_rows }
    [void]$markdown.AppendLine('| ' + $row.language.ToUpperInvariant() + ' | ' + $variantLabels[$row.config] + ' | ' +
        (Format-Number $row.train_seconds 3) + ' | ' + (Format-Number $row.predict_seconds 3) + ' | N/A, unlabeled | ' +
        (Metric-Text $row) + ' | ' + $scope + ' |')
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('KMeans assessment includes both training and heldout scoring/summaries. Hierarchical/DBSCAN assessment is sample silhouette/noise. Autoencoder prediction includes heldout transformation and reconstruction. Fit figures repeat shared prerequisites and must not be summed across pipelines. Detailed timing scope is recorded in JSON/CSV.')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('## Chart coverage')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('| Family | Measured variants | Evidence |')
[void]$markdown.AppendLine('| --- | --- | --- |')
foreach ($family in $familyRows) {
    $evidence = if ($family.kind -eq 'classification') { 'all six Romanian tasks / 3 folds' } else { 'Romanian and English / unlabeled' }
    [void]$markdown.AppendLine('| ' + $family.label + ' | ' + (($family.variants | ForEach-Object { $variantLabels[$_] }) -join ', ') + ' | ' + $evidence + ' |')
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('## Space: dimensions, parameters, process memory and storage')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('Working sets are **shared-process snapshots**, not isolated model RAM. Lifetime peaks include earlier trials and retained/native state, so memory rows cannot rank algorithms. Classifier values are maxima across three folds; feature/vocabulary ranges show actual training-fitted sizes. MiB = 1,048,576 bytes. Neural parameter bytes exclude training state and activations. Unexported classifier/autoencoder disk size is N/A. Book bytes are private pipeline ZIPs or training-sample JSON and are not comparable model types.')
[void]$markdown.AppendLine()
Add-SpaceTable $markdown @($classificationRows.ToArray() + $bookRows.ToArray())
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('## Interpretation and timing limits')
[void]$markdown.AppendLine()
foreach ($limit in $limits) { [void]$markdown.AppendLine('- ' + $limit) }
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('Source report SHA256s are preserved in `algorithm-comparison.json`. This summary contains aggregate evidence only.')

$csv = @($classificationRows.ToArray() + $bookRows.ToArray() | ForEach-Object {
    $row = $_; $notes = $row.notes
    if ($row.task -eq 'unlabeled_book_exploration') { $notes += ' ' + $row.timing_scope + ' Split: ' + $row.split_kind }
    [pscustomobject][ordered]@{
        config = $row.config; task = $row.task; language = $row.language; status = $row.status
        train_seconds = ([double]$row.train_seconds).ToString('G17', $culture)
        predict_seconds = ([double]$row.predict_seconds).ToString('G17', $culture)
        batched_ms_per_row = if ($null -eq $row.batched_ms_per_row) { '' } else { ([double]$row.batched_ms_per_row).ToString('G17', $culture) }
        batched_rows_per_second = if ($null -eq $row.batched_rows_per_second) { '' } else { ([double]$row.batched_rows_per_second).ToString('G17', $culture) }
        accuracy_fraction = if ($null -eq $row.accuracy_fraction) { '' } else { ([double]$row.accuracy_fraction).ToString('G17', $culture) }
        macro_f1_fraction = if ($null -eq $row.macro_f1_fraction) { '' } else { ([double]$row.macro_f1_fraction).ToString('G17', $culture) }
        unsupervised_metric = if ($row.task -eq 'unlabeled_book_exploration') { Metric-Text $row } else { '' }
        feature_dimensions_min = $row.space.feature_dimensions_min; feature_dimensions_max = $row.space.feature_dimensions_max
        training_vocabulary_size_min = $row.space.training_vocabulary_size_min; training_vocabulary_size_max = $row.space.training_vocabulary_size_max
        maximum_sequence_length = $row.space.maximum_sequence_length
        process_working_set_before_min_mb = $row.space.process_working_set_before_min_mb
        process_working_set_after_max_mb = $row.space.process_working_set_after_max_mb
        process_lifetime_peak_working_set_max_mb = $row.space.process_lifetime_peak_working_set_max_mb
        neural_parameter_count_min = $row.space.neural_parameter_count_min; neural_parameter_count_max = $row.space.neural_parameter_count_max
        float32_parameter_bytes_max = $row.space.float32_parameter_bytes_max; artifact_bytes = $row.space.artifact_bytes
        space_scope = $row.space.scope
        notes = $notes
    }
})

$destination = [IO.Path]::GetFullPath($OutputDirectory)
$inputPaths = @($ClassificationReportPath, $RomanianBookReportPath, $EnglishBookReportPath | ForEach-Object { [IO.Path]::GetFullPath($_) })
$outputs = @('algorithm-comparison.md', 'algorithm-comparison.csv', 'algorithm-comparison.json' | ForEach-Object { Join-Path $destination $_ })
foreach ($file in $outputs) {
    if (@($inputPaths | Where-Object { [string]::Equals($_, $file, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw 'A summary destination must not overwrite its input report.'
    }
}
[void][IO.Directory]::CreateDirectory($destination)
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($outputs[0], $markdown.ToString().Replace("`r`n", "`n"), $encoding)
[IO.File]::WriteAllText($outputs[1], (($csv | ConvertTo-Csv -NoTypeInformation -UseQuotes Always) -join "`n") + "`n", $encoding)
[IO.File]::WriteAllText($outputs[2], ($release | ConvertTo-Json -Depth 60) + "`n", $encoding)
Write-Output 'Generated completed comparison: 17 families, 19 variants, 84 classifier task measurements and 10 unlabeled book measurements.'
