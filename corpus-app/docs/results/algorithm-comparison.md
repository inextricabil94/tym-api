# Algorithm comparison

Measured coverage: **17 chart families / 19 variants**: 14 classification variants from 12 families, plus five unlabeled book pipelines in Romanian and English.

Classification uses the same three document-component folds. Fit seconds are the **sum of three independently fitted models**, not a single final fit. Accuracy and macro F1 are diagnostic conditional label scores on supplied annotations with unknown adjudication.

Feature budgets are fixed before this run: native/numeric models use train-only word unigrams/bigrams and character trigrams, each capped at 1024 per channel (dimension <= 3 x 1024 x active channels). Scratch neural models use a train-only vocabulary <= 2048 including padding/unknown, first 64 tokens, five epochs. This is a resource-bounded comparison with different representations.

## Temporal links: all classification variants

| Variant | Accuracy % | Macro F1 % | Sum 3-fold fit s | Sum prediction s | Batch ms/row | Batch rows/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Linear regression OVR | 40.04 | 20.40 | 4.828 | 0.170 | 0.03536 | 28284.03 |
| Logistic regression SDCA | 41.10 | 22.95 | 3.773 | 0.060 | 0.01250 | 79985.09 |
| Logistic regression L-BFGS | 40.85 | 20.37 | 10.663 | 0.055 | 0.01148 | 87077.94 |
| Decision tree CART | 28.99 | 9.68 | 0.739 | 0.095 | 0.01986 | 50348.24 |
| Random forest OVA | 40.00 | 22.76 | 419.323 | 0.290 | 0.06025 | 16598.08 |
| Gradient boosting FastTree OVA | 40.79 | 20.99 | 936.325 | 0.404 | 0.08403 | 11900.02 |
| Gradient boosting LightGBM | 41.29 | 20.29 | 126.055 | 0.348 | 0.07232 | 13827.53 |
| Linear SVM OVA | 42.08 | 22.99 | 15.704 | 0.100 | 0.02081 | 48060.39 |
| KNN | 39.41 | 21.54 | 0.297 | 26.618 | 5.53627 | 180.63 |
| Naive Bayes | 29.99 | 6.00 | 0.261 | 0.131 | 0.02723 | 36721.10 |
| Neural network MLP | 28.51 | 5.76 | 15.708 | 1.312 | 0.27278 | 3665.99 |
| Text CNN | 29.10 | 8.65 | 17.386 | 1.445 | 0.30047 | 3328.07 |
| LSTM | 26.77 | 6.90 | 21.081 | 1.596 | 0.33202 | 3011.86 |
| Scratch Transformer | 28.72 | 8.76 | 31.792 | 2.112 | 0.43921 | 2276.81 |

TLINK training-fold majority baseline: accuracy 21.07%; full-inventory macro F1 4.03%.

## Complete six-task measurements

### timebank_event_class

Rows: 5947. Language: Romanian. Every row is scored once out of fold.

| Variant | Accuracy % | Macro F1 % | Sum 3-fold fit s | Sum prediction s | Batch ms/row | Batch rows/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Linear regression OVR | 70.56 | 49.35 | 1.138 | 0.106 | 0.01786 | 55979.98 |
| Logistic regression SDCA | 72.20 | 58.24 | 1.203 | 0.040 | 0.00668 | 149644.32 |
| Logistic regression L-BFGS | 70.09 | 46.51 | 2.436 | 0.046 | 0.00773 | 129408.08 |
| Decision tree CART | 66.18 | 37.86 | 0.739 | 0.071 | 0.01200 | 83336.25 |
| Random forest OVA | 70.39 | 56.60 | 158.732 | 0.152 | 0.02560 | 39056.68 |
| Gradient boosting FastTree OVA | 70.91 | 56.50 | 355.969 | 0.238 | 0.04007 | 24956.53 |
| Gradient boosting LightGBM | 70.83 | 53.69 | 46.785 | 0.237 | 0.03990 | 25060.64 |
| Linear SVM OVA | 71.70 | 55.02 | 6.667 | 0.064 | 0.01076 | 92968.07 |
| KNN | 69.11 | 55.90 | 0.176 | 21.420 | 3.60177 | 277.64 |
| Naive Bayes | 59.74 | 21.25 | 0.179 | 0.079 | 0.01326 | 75394.82 |
| Neural network MLP | 51.96 | 9.77 | 18.605 | 1.627 | 0.27356 | 3655.46 |
| Text CNN | 64.97 | 31.81 | 21.380 | 1.799 | 0.30247 | 3306.08 |
| LSTM | 56.05 | 18.33 | 26.790 | 1.923 | 0.32334 | 3092.76 |
| Scratch Transformer | 62.15 | 27.26 | 39.731 | 2.624 | 0.44128 | 2266.12 |

Training-fold majority baseline: accuracy 51.96%; macro F1 9.77%.

### timebank_event_tense

Rows: 5920. Language: Romanian. Every row is scored once out of fold.

| Variant | Accuracy % | Macro F1 % | Sum 3-fold fit s | Sum prediction s | Batch ms/row | Batch rows/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Linear regression OVR | 76.44 | 60.77 | 1.139 | 0.110 | 0.01851 | 54026.23 |
| Logistic regression SDCA | 77.38 | 63.20 | 0.912 | 0.037 | 0.00624 | 160356.25 |
| Logistic regression L-BFGS | 75.73 | 58.74 | 2.681 | 0.038 | 0.00649 | 154039.90 |
| Decision tree CART | 58.16 | 33.48 | 0.747 | 0.067 | 0.01124 | 88945.65 |
| Random forest OVA | 76.71 | 64.80 | 154.628 | 0.144 | 0.02424 | 41251.34 |
| Gradient boosting FastTree OVA | 79.14 | 65.44 | 347.615 | 0.206 | 0.03486 | 28687.31 |
| Gradient boosting LightGBM | 78.72 | 64.64 | 50.344 | 0.227 | 0.03831 | 26104.58 |
| Linear SVM OVA | 77.48 | 62.76 | 6.462 | 0.230 | 0.03882 | 25762.03 |
| KNN | 69.61 | 50.79 | 0.182 | 21.662 | 3.65907 | 273.29 |
| Naive Bayes | 58.41 | 23.49 | 0.191 | 0.072 | 0.01215 | 82307.85 |
| Neural network MLP | 46.11 | 16.35 | 18.176 | 1.652 | 0.27907 | 3583.38 |
| Text CNN | 68.21 | 45.91 | 20.861 | 1.727 | 0.29174 | 3427.71 |
| LSTM | 46.82 | 17.57 | 25.042 | 1.846 | 0.31185 | 3206.71 |
| Scratch Transformer | 56.64 | 30.23 | 39.210 | 2.460 | 0.41561 | 2406.10 |

Training-fold majority baseline: accuracy 35.05%; macro F1 7.42%.

### timebank_timex_type

Rows: 1144. Language: Romanian. Every row is scored once out of fold.

| Variant | Accuracy % | Macro F1 % | Sum 3-fold fit s | Sum prediction s | Batch ms/row | Batch rows/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Linear regression OVR | 92.48 | 63.23 | 0.340 | 0.025 | 0.02213 | 45181.67 |
| Logistic regression SDCA | 91.96 | 61.40 | 0.141 | 0.025 | 0.02167 | 46139.45 |
| Logistic regression L-BFGS | 88.20 | 44.45 | 0.273 | 0.043 | 0.03715 | 26914.29 |
| Decision tree CART | 87.24 | 45.58 | 0.139 | 0.028 | 0.02425 | 41242.76 |
| Random forest OVA | 90.21 | 56.11 | 31.799 | 0.046 | 0.03984 | 25100.71 |
| Gradient boosting FastTree OVA | 90.38 | 59.37 | 77.934 | 0.057 | 0.04964 | 20146.13 |
| Gradient boosting LightGBM | 90.12 | 55.44 | 4.869 | 0.044 | 0.03851 | 25968.42 |
| Linear SVM OVA | 92.22 | 72.11 | 0.988 | 0.027 | 0.02344 | 42661.73 |
| KNN | 91.17 | 63.83 | 0.053 | 0.741 | 0.64807 | 1543.04 |
| Naive Bayes | 81.91 | 22.51 | 0.054 | 0.024 | 0.02093 | 47787.33 |
| Neural network MLP | 81.91 | 22.51 | 3.802 | 0.314 | 0.27428 | 3645.88 |
| Text CNN | 81.91 | 22.51 | 4.049 | 0.334 | 0.29185 | 3426.42 |
| LSTM | 81.91 | 22.51 | 4.895 | 0.362 | 0.31615 | 3163.03 |
| Scratch Transformer | 82.17 | 25.05 | 7.611 | 0.506 | 0.44245 | 2260.12 |

Training-fold majority baseline: accuracy 81.91%; macro F1 22.51%.

### timebank_tlink

Rows: 4808. Language: Romanian. Every row is scored once out of fold.

| Variant | Accuracy % | Macro F1 % | Sum 3-fold fit s | Sum prediction s | Batch ms/row | Batch rows/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Linear regression OVR | 40.04 | 20.40 | 4.828 | 0.170 | 0.03536 | 28284.03 |
| Logistic regression SDCA | 41.10 | 22.95 | 3.773 | 0.060 | 0.01250 | 79985.09 |
| Logistic regression L-BFGS | 40.85 | 20.37 | 10.663 | 0.055 | 0.01148 | 87077.94 |
| Decision tree CART | 28.99 | 9.68 | 0.739 | 0.095 | 0.01986 | 50348.24 |
| Random forest OVA | 40.00 | 22.76 | 419.323 | 0.290 | 0.06025 | 16598.08 |
| Gradient boosting FastTree OVA | 40.79 | 20.99 | 936.325 | 0.404 | 0.08403 | 11900.02 |
| Gradient boosting LightGBM | 41.29 | 20.29 | 126.055 | 0.348 | 0.07232 | 13827.53 |
| Linear SVM OVA | 42.08 | 22.99 | 15.704 | 0.100 | 0.02081 | 48060.39 |
| KNN | 39.41 | 21.54 | 0.297 | 26.618 | 5.53627 | 180.63 |
| Naive Bayes | 29.99 | 6.00 | 0.261 | 0.131 | 0.02723 | 36721.10 |
| Neural network MLP | 28.51 | 5.76 | 15.708 | 1.312 | 0.27278 | 3665.99 |
| Text CNN | 29.10 | 8.65 | 17.386 | 1.445 | 0.30047 | 3328.07 |
| LSTM | 26.77 | 6.90 | 21.081 | 1.596 | 0.33202 | 3011.86 |
| Scratch Transformer | 28.72 | 8.76 | 31.792 | 2.112 | 0.43921 | 2276.81 |

Training-fold majority baseline: accuracy 21.07%; macro F1 4.03%.

### timebank_slink

Rows: 2133. Language: Romanian. Every row is scored once out of fold.

| Variant | Accuracy % | Macro F1 % | Sum 3-fold fit s | Sum prediction s | Batch ms/row | Batch rows/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Linear regression OVR | 83.54 | 67.50 | 1.934 | 0.089 | 0.04184 | 23900.45 |
| Logistic regression SDCA | 83.26 | 64.54 | 0.827 | 0.054 | 0.02548 | 39240.43 |
| Logistic regression L-BFGS | 80.68 | 50.32 | 2.304 | 0.051 | 0.02395 | 41750.18 |
| Decision tree CART | 75.53 | 48.01 | 0.309 | 0.069 | 0.03237 | 30896.48 |
| Random forest OVA | 83.03 | 73.88 | 117.718 | 0.089 | 0.04166 | 24004.83 |
| Gradient boosting FastTree OVA | 83.50 | 72.92 | 358.098 | 0.119 | 0.05601 | 17853.48 |
| Gradient boosting LightGBM | 82.42 | 67.41 | 31.223 | 0.097 | 0.04557 | 21945.32 |
| Linear SVM OVA | 85.51 | 76.73 | 3.984 | 0.065 | 0.03027 | 33032.02 |
| KNN | 79.42 | 71.02 | 0.157 | 6.508 | 3.05101 | 327.76 |
| Naive Bayes | 75.86 | 27.73 | 0.157 | 0.095 | 0.04433 | 22556.49 |
| Neural network MLP | 56.35 | 20.08 | 7.067 | 0.623 | 0.29204 | 3424.21 |
| Text CNN | 72.62 | 27.42 | 7.733 | 0.679 | 0.31834 | 3141.27 |
| LSTM | 46.51 | 16.71 | 9.583 | 0.711 | 0.33340 | 2999.42 |
| Scratch Transformer | 72.71 | 26.58 | 13.923 | 0.976 | 0.45780 | 2184.34 |

Training-fold majority baseline: accuracy 41.12%; macro F1 14.51%.

### timebank_alink

Rows: 199. Language: Romanian. Every row is scored once out of fold.

| Variant | Accuracy % | Macro F1 % | Sum 3-fold fit s | Sum prediction s | Batch ms/row | Batch rows/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Linear regression OVR | 63.82 | 61.64 | 0.325 | 0.035 | 0.17454 | 5729.21 |
| Logistic regression SDCA | 61.31 | 54.16 | 0.620 | 0.047 | 0.23864 | 4190.46 |
| Logistic regression L-BFGS | 54.27 | 42.97 | 0.752 | 0.056 | 0.28309 | 3532.50 |
| Decision tree CART | 60.80 | 53.42 | 0.170 | 0.024 | 0.11923 | 8387.46 |
| Random forest OVA | 64.82 | 62.17 | 19.767 | 0.073 | 0.36760 | 2720.35 |
| Gradient boosting FastTree OVA | 65.33 | 61.14 | 89.598 | 0.046 | 0.22909 | 4365.15 |
| Gradient boosting LightGBM | 62.31 | 58.21 | 3.328 | 0.036 | 0.17851 | 5601.99 |
| Linear SVM OVA | 68.34 | 63.06 | 0.863 | 0.068 | 0.34125 | 2930.37 |
| KNN | 62.31 | 55.70 | 0.079 | 0.151 | 0.75910 | 1317.35 |
| Naive Bayes | 35.18 | 11.83 | 0.161 | 0.065 | 0.32669 | 3060.98 |
| Neural network MLP | 17.59 | 12.85 | 0.892 | 0.088 | 0.44400 | 2252.28 |
| Text CNN | 31.66 | 13.98 | 0.894 | 0.083 | 0.41538 | 2407.45 |
| LSTM | 28.14 | 16.06 | 1.019 | 0.083 | 0.41685 | 2398.97 |
| Scratch Transformer | 33.67 | 10.08 | 1.476 | 0.110 | 0.55317 | 1807.77 |

Training-fold majority baseline: accuracy 34.17%; macro F1 10.19%.

## Books: unlabeled exploration

- RO: 5213 passages, 14 documents, 4037 training / 1176 heldout; `complete_document_component_holdout`.
- EN: 446 passages, 1 documents, 356 training / 90 heldout; `within_source_work_normalized_passage_holdout`.

| Language | Pipeline | Fit s, including prerequisites | Prediction/assessment s | Accuracy | Metrics | Scope |
| --- | --- | ---: | ---: | --- | --- | --- |
| RO | KMeans | 39.296 | 4.314 | N/A, unlabeled | heldout distance=0.41793; DBI=4.9256 | shared holdout n=1176 |
| RO | PCA + KMeans | 47.843 | 2.670 | N/A, unlabeled | heldout distance=0.85429; DBI=3.0448; PCA basis fit s=8.448 | shared holdout n=1176 |
| RO | Average-linkage hierarchical | 10.746 | 0.034 | N/A, unlabeled | sample clusters=8; noise=0.00%; silhouette=0.0664 | training sample n=256 |
| RO | DBSCAN | 10.732 | 0.000 | N/A, unlabeled | sample clusters=0; noise=100.00%; silhouette=N/A | training sample n=256 |
| RO | Numeric autoencoder | 16.975 | 0.446 | N/A, unlabeled | heldout MSE=0.008020; mean baseline=0.031065; zero baseline=0.031250 | shared holdout n=1176 |
| EN | KMeans | 2.678 | 0.585 | N/A, unlabeled | heldout distance=0.39195; DBI=2.5466 | shared holdout n=90 |
| EN | PCA + KMeans | 4.474 | 0.593 | N/A, unlabeled | heldout distance=0.68199; DBI=2.3482; PCA basis fit s=1.362 | shared holdout n=90 |
| EN | Average-linkage hierarchical | 1.923 | 0.034 | N/A, unlabeled | sample clusters=8; noise=0.00%; silhouette=0.0615 | training sample n=256 |
| EN | DBSCAN | 1.915 | 0.000 | N/A, unlabeled | sample clusters=0; noise=100.00%; silhouette=N/A | training sample n=256 |
| EN | Numeric autoencoder | 2.505 | 0.067 | N/A, unlabeled | heldout MSE=0.025232; mean baseline=0.031259; zero baseline=0.031250 | shared holdout n=90 |

KMeans assessment includes both training and heldout scoring/summaries. Hierarchical/DBSCAN assessment is sample silhouette/noise. Autoencoder prediction includes heldout transformation and reconstruction. Fit figures repeat shared prerequisites and must not be summed across pipelines. Detailed timing scope is recorded in JSON/CSV.

## Chart coverage

| Family | Measured variants | Evidence |
| --- | --- | --- |
| Linear regression | Linear regression OVR | all six Romanian tasks / 3 folds |
| Logistic regression | Logistic regression SDCA, Logistic regression L-BFGS | all six Romanian tasks / 3 folds |
| Decision tree | Decision tree CART | all six Romanian tasks / 3 folds |
| Random forest | Random forest OVA | all six Romanian tasks / 3 folds |
| Gradient boosting | Gradient boosting FastTree OVA, Gradient boosting LightGBM | all six Romanian tasks / 3 folds |
| SVM | Linear SVM OVA | all six Romanian tasks / 3 folds |
| KNN | KNN | all six Romanian tasks / 3 folds |
| Naive Bayes | Naive Bayes | all six Romanian tasks / 3 folds |
| KMeans | KMeans | Romanian and English / unlabeled |
| Hierarchical clustering | Average-linkage hierarchical | Romanian and English / unlabeled |
| PCA | PCA + KMeans | Romanian and English / unlabeled |
| Neural network MLP | Neural network MLP | all six Romanian tasks / 3 folds |
| CNN | Text CNN | all six Romanian tasks / 3 folds |
| RNN | LSTM | all six Romanian tasks / 3 folds |
| Transformer | Scratch Transformer | all six Romanian tasks / 3 folds |
| Autoencoder | Numeric autoencoder | Romanian and English / unlabeled |
| DBSCAN | DBSCAN | Romanian and English / unlabeled |

## Space: dimensions, parameters, process memory and storage

Working sets are **shared-process snapshots**, not isolated model RAM. Lifetime peaks include earlier trials and retained/native state, so memory rows cannot rank algorithms. Classifier values are maxima across three folds; feature/vocabulary ranges show actual training-fitted sizes. MiB = 1,048,576 bytes. Neural parameter bytes exclude training state and activations. Unexported classifier/autoencoder disk size is N/A. Book bytes are private pipeline ZIPs or training-sample JSON and are not comparable model types.

| Task/language | Variant | Feature dimension range | Vocabulary range | Parameters max | Float32 parameter MiB | Shared WS after max MiB | Process lifetime peak max MiB | Private artifact bytes |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| timebank_event_class/ro | Linear regression OVR | 5453..5469 | N/A..N/A | N/A | N/A | 363.87 | 396.55 | N/A |
| timebank_event_class/ro | Logistic regression SDCA | 5453..5469 | N/A..N/A | N/A | N/A | 299.27 | 396.55 | N/A |
| timebank_event_class/ro | Logistic regression L-BFGS | 5453..5469 | N/A..N/A | N/A | N/A | 306.98 | 396.55 | N/A |
| timebank_event_class/ro | Decision tree CART | 5453..5469 | N/A..N/A | N/A | N/A | 380.71 | 396.55 | N/A |
| timebank_event_class/ro | Random forest OVA | 5453..5469 | N/A..N/A | N/A | N/A | 282.98 | 396.55 | N/A |
| timebank_event_class/ro | Gradient boosting FastTree OVA | 5453..5469 | N/A..N/A | N/A | N/A | 306.20 | 396.55 | N/A |
| timebank_event_class/ro | Gradient boosting LightGBM | 5453..5469 | N/A..N/A | N/A | N/A | 342.86 | 396.55 | N/A |
| timebank_event_class/ro | Linear SVM OVA | 5453..5469 | N/A..N/A | N/A | N/A | 295.27 | 396.55 | N/A |
| timebank_event_class/ro | KNN | 5453..5469 | N/A..N/A | N/A | N/A | 380.64 | 396.55 | N/A |
| timebank_event_class/ro | Naive Bayes | 5453..5469 | N/A..N/A | N/A | N/A | 305.66 | 396.55 | N/A |
| timebank_event_class/ro | Neural network MLP | N/A..N/A | 2048..2048 | 131591 | 0.5020 | 291.60 | 396.55 | N/A |
| timebank_event_class/ro | Text CNN | N/A..N/A | 2048..2048 | 68871 | 0.2627 | 303.04 | 396.55 | N/A |
| timebank_event_class/ro | LSTM | N/A..N/A | 2048..2048 | 74215 | 0.2831 | 289.95 | 396.55 | N/A |
| timebank_event_class/ro | Scratch Transformer | N/A..N/A | 2048..2048 | 76359 | 0.2913 | 292.75 | 396.55 | N/A |
| timebank_event_tense/ro | Linear regression OVR | 5453..5469 | N/A..N/A | N/A | N/A | 375.95 | 414.44 | N/A |
| timebank_event_tense/ro | Logistic regression SDCA | 5453..5469 | N/A..N/A | N/A | N/A | 303.93 | 406.52 | N/A |
| timebank_event_tense/ro | Logistic regression L-BFGS | 5453..5469 | N/A..N/A | N/A | N/A | 298.73 | 406.52 | N/A |
| timebank_event_tense/ro | Decision tree CART | 5453..5469 | N/A..N/A | N/A | N/A | 387.98 | 414.44 | N/A |
| timebank_event_tense/ro | Random forest OVA | 5453..5469 | N/A..N/A | N/A | N/A | 314.32 | 406.52 | N/A |
| timebank_event_tense/ro | Gradient boosting FastTree OVA | 5453..5469 | N/A..N/A | N/A | N/A | 333.98 | 406.52 | N/A |
| timebank_event_tense/ro | Gradient boosting LightGBM | 5453..5469 | N/A..N/A | N/A | N/A | 365.03 | 414.44 | N/A |
| timebank_event_tense/ro | Linear SVM OVA | 5453..5469 | N/A..N/A | N/A | N/A | 304.60 | 406.52 | N/A |
| timebank_event_tense/ro | KNN | 5453..5469 | N/A..N/A | N/A | N/A | 397.60 | 414.44 | N/A |
| timebank_event_tense/ro | Naive Bayes | 5453..5469 | N/A..N/A | N/A | N/A | 299.91 | 406.52 | N/A |
| timebank_event_tense/ro | Neural network MLP | N/A..N/A | 2048..2048 | 131591 | 0.5020 | 295.11 | 414.44 | N/A |
| timebank_event_tense/ro | Text CNN | N/A..N/A | 2048..2048 | 68871 | 0.2627 | 309.80 | 414.44 | N/A |
| timebank_event_tense/ro | LSTM | N/A..N/A | 2048..2048 | 74215 | 0.2831 | 302.68 | 414.44 | N/A |
| timebank_event_tense/ro | Scratch Transformer | N/A..N/A | 2048..2048 | 76359 | 0.2913 | 316.63 | 414.44 | N/A |
| timebank_timex_type/ro | Linear regression OVR | 4762..4793 | N/A..N/A | N/A | N/A | 288.42 | 434.96 | N/A |
| timebank_timex_type/ro | Logistic regression SDCA | 4762..4793 | N/A..N/A | N/A | N/A | 315.84 | 434.96 | N/A |
| timebank_timex_type/ro | Logistic regression L-BFGS | 4762..4793 | N/A..N/A | N/A | N/A | 317.55 | 434.96 | N/A |
| timebank_timex_type/ro | Decision tree CART | 4762..4793 | N/A..N/A | N/A | N/A | 289.12 | 434.96 | N/A |
| timebank_timex_type/ro | Random forest OVA | 4762..4793 | N/A..N/A | N/A | N/A | 283.49 | 434.96 | N/A |
| timebank_timex_type/ro | Gradient boosting FastTree OVA | 4762..4793 | N/A..N/A | N/A | N/A | 276.73 | 434.96 | N/A |
| timebank_timex_type/ro | Gradient boosting LightGBM | 4762..4793 | N/A..N/A | N/A | N/A | 284.01 | 434.96 | N/A |
| timebank_timex_type/ro | Linear SVM OVA | 4762..4793 | N/A..N/A | N/A | N/A | 302.13 | 434.96 | N/A |
| timebank_timex_type/ro | KNN | 4762..4793 | N/A..N/A | N/A | N/A | 290.33 | 434.96 | N/A |
| timebank_timex_type/ro | Naive Bayes | 4762..4793 | N/A..N/A | N/A | N/A | 317.56 | 434.96 | N/A |
| timebank_timex_type/ro | Neural network MLP | N/A..N/A | 2048..2048 | 131396 | 0.5012 | 315.07 | 434.96 | N/A |
| timebank_timex_type/ro | Text CNN | N/A..N/A | 2048..2048 | 68772 | 0.2623 | 314.03 | 434.96 | N/A |
| timebank_timex_type/ro | LSTM | N/A..N/A | 2048..2048 | 74116 | 0.2827 | 314.49 | 434.96 | N/A |
| timebank_timex_type/ro | Scratch Transformer | N/A..N/A | 2048..2048 | 76260 | 0.2909 | 309.68 | 434.96 | N/A |
| timebank_tlink/ro | Linear regression OVR | 11571..11680 | N/A..N/A | N/A | N/A | 489.74 | 560.73 | N/A |
| timebank_tlink/ro | Logistic regression SDCA | 11571..11680 | N/A..N/A | N/A | N/A | 319.95 | 555.99 | N/A |
| timebank_tlink/ro | Logistic regression L-BFGS | 11571..11680 | N/A..N/A | N/A | N/A | 330.58 | 555.99 | N/A |
| timebank_tlink/ro | Decision tree CART | 11571..11680 | N/A..N/A | N/A | N/A | 514.66 | 560.73 | N/A |
| timebank_tlink/ro | Random forest OVA | 11571..11680 | N/A..N/A | N/A | N/A | 345.89 | 555.99 | N/A |
| timebank_tlink/ro | Gradient boosting FastTree OVA | 11571..11680 | N/A..N/A | N/A | N/A | 406.07 | 555.99 | N/A |
| timebank_tlink/ro | Gradient boosting LightGBM | 11571..11680 | N/A..N/A | N/A | N/A | 464.21 | 560.73 | N/A |
| timebank_tlink/ro | Linear SVM OVA | 11571..11680 | N/A..N/A | N/A | N/A | 352.33 | 555.99 | N/A |
| timebank_tlink/ro | KNN | 11571..11680 | N/A..N/A | N/A | N/A | 539.25 | 560.73 | N/A |
| timebank_tlink/ro | Naive Bayes | 11571..11680 | N/A..N/A | N/A | N/A | 351.23 | 555.99 | N/A |
| timebank_tlink/ro | Neural network MLP | N/A..N/A | 2048..2048 | 132046 | 0.5037 | 322.96 | 560.73 | N/A |
| timebank_tlink/ro | Text CNN | N/A..N/A | 2048..2048 | 69102 | 0.2636 | 336.62 | 560.73 | N/A |
| timebank_tlink/ro | LSTM | N/A..N/A | 2048..2048 | 74446 | 0.2840 | 323.79 | 560.73 | N/A |
| timebank_tlink/ro | Scratch Transformer | N/A..N/A | 2048..2048 | 76590 | 0.2922 | 343.82 | 560.73 | N/A |
| timebank_slink/ro | Linear regression OVR | 10048..10292 | N/A..N/A | N/A | N/A | 375.85 | 429.42 | N/A |
| timebank_slink/ro | Logistic regression SDCA | 10048..10292 | N/A..N/A | N/A | N/A | 317.32 | 424.85 | N/A |
| timebank_slink/ro | Logistic regression L-BFGS | 10048..10292 | N/A..N/A | N/A | N/A | 317.13 | 424.85 | N/A |
| timebank_slink/ro | Decision tree CART | 10048..10292 | N/A..N/A | N/A | N/A | 383.43 | 429.42 | N/A |
| timebank_slink/ro | Random forest OVA | 10048..10292 | N/A..N/A | N/A | N/A | 360.45 | 424.85 | N/A |
| timebank_slink/ro | Gradient boosting FastTree OVA | 10048..10292 | N/A..N/A | N/A | N/A | 348.32 | 424.85 | N/A |
| timebank_slink/ro | Gradient boosting LightGBM | 10048..10292 | N/A..N/A | N/A | N/A | 367.26 | 429.42 | N/A |
| timebank_slink/ro | Linear SVM OVA | 10048..10292 | N/A..N/A | N/A | N/A | 313.64 | 424.85 | N/A |
| timebank_slink/ro | KNN | 10048..10292 | N/A..N/A | N/A | N/A | 384.57 | 429.42 | N/A |
| timebank_slink/ro | Naive Bayes | 10048..10292 | N/A..N/A | N/A | N/A | 317.17 | 424.85 | N/A |
| timebank_slink/ro | Neural network MLP | N/A..N/A | 2048..2048 | 131526 | 0.5017 | 311.50 | 434.96 | N/A |
| timebank_slink/ro | Text CNN | N/A..N/A | 2048..2048 | 68838 | 0.2626 | 314.22 | 434.96 | N/A |
| timebank_slink/ro | LSTM | N/A..N/A | 2048..2048 | 74182 | 0.2830 | 313.11 | 434.96 | N/A |
| timebank_slink/ro | Scratch Transformer | N/A..N/A | 2048..2048 | 76326 | 0.2912 | 314.13 | 434.96 | N/A |
| timebank_alink/ro | Linear regression OVR | 7272..7304 | N/A..N/A | N/A | N/A | 248.73 | 270.01 | N/A |
| timebank_alink/ro | Logistic regression SDCA | 7272..7304 | N/A..N/A | N/A | N/A | 262.50 | 270.01 | N/A |
| timebank_alink/ro | Logistic regression L-BFGS | 7272..7304 | N/A..N/A | N/A | N/A | 262.99 | 270.01 | N/A |
| timebank_alink/ro | Decision tree CART | 7272..7304 | N/A..N/A | N/A | N/A | 249.24 | 270.01 | N/A |
| timebank_alink/ro | Random forest OVA | 7272..7304 | N/A..N/A | N/A | N/A | 237.81 | 270.01 | N/A |
| timebank_alink/ro | Gradient boosting FastTree OVA | 7272..7304 | N/A..N/A | N/A | N/A | 236.52 | 270.01 | N/A |
| timebank_alink/ro | Gradient boosting LightGBM | 7272..7304 | N/A..N/A | N/A | N/A | 242.13 | 270.01 | N/A |
| timebank_alink/ro | Linear SVM OVA | 7272..7304 | N/A..N/A | N/A | N/A | 262.23 | 270.01 | N/A |
| timebank_alink/ro | KNN | 7272..7304 | N/A..N/A | N/A | N/A | 249.50 | 270.01 | N/A |
| timebank_alink/ro | Naive Bayes | 7272..7304 | N/A..N/A | N/A | N/A | 264.29 | 270.01 | N/A |
| timebank_alink/ro | Neural network MLP | N/A..N/A | 2048..2048 | 131461 | 0.5015 | 262.84 | 270.01 | N/A |
| timebank_alink/ro | Text CNN | N/A..N/A | 2048..2048 | 68805 | 0.2625 | 265.52 | 270.01 | N/A |
| timebank_alink/ro | LSTM | N/A..N/A | 2048..2048 | 74149 | 0.2829 | 271.62 | 271.64 | N/A |
| timebank_alink/ro | Scratch Transformer | N/A..N/A | 2048..2048 | 76293 | 0.2910 | 274.41 | 274.41 | N/A |
| unlabeled_book_exploration/ro | KMeans | 706013..706013 | N/A..N/A | N/A | N/A | 401.09 | 433.76 | 23035716 |
| unlabeled_book_exploration/ro | PCA + KMeans | 32..32 | N/A..N/A | N/A | N/A | 553.10 | 559.57 | 40632685 |
| unlabeled_book_exploration/ro | Average-linkage hierarchical | 32..32 | N/A..N/A | N/A | N/A | 643.28 | 646.50 | 194965 |
| unlabeled_book_exploration/ro | DBSCAN | 32..32 | N/A..N/A | N/A | N/A | 643.30 | 646.50 | 195199 |
| unlabeled_book_exploration/ro | Numeric autoencoder | 32..32 | N/A..N/A | 6320 | 0.0241 | 641.76 | 646.50 | N/A |
| unlabeled_book_exploration/en | KMeans | 80599..80599 | N/A..N/A | N/A | N/A | 132.10 | 132.10 | 2444504 |
| unlabeled_book_exploration/en | PCA + KMeans | 32..32 | N/A..N/A | N/A | N/A | 138.63 | 139.27 | 4780823 |
| unlabeled_book_exploration/en | Average-linkage hierarchical | 32..32 | N/A..N/A | N/A | N/A | 237.96 | 237.96 | 195143 |
| unlabeled_book_exploration/en | DBSCAN | 32..32 | N/A..N/A | N/A | N/A | 238.72 | 238.72 | 195377 |
| unlabeled_book_exploration/en | Numeric autoencoder | 32..32 | N/A..N/A | 6320 | 0.0241 | 234.50 | 234.50 | N/A |

## Interpretation and timing limits

- Seventeen chart families, nineteen distinct implemented variants: fourteen classifiers from twelve families plus five unlabeled exploration pipelines. PCA is measured as PCA+KMeans, not as a semantic classifier.
- Classification accuracy and macro F1 are pooled out-of-fold metrics on supplied Romanian annotations of unknown adjudication, conditional on supplied mentions/endpoints. They do not measure extraction or complete graph accuracy.
- Macro F1 uses the fixed full task label inventory, retaining rare labels absent from some training folds with zero F1 when unrecovered. Training-fold majority baselines are retained for each task.
- Each classification fit time is the SUM of three separately fitted folds, not one final production-model fit. Feature fitting/training vectorization is included; CSV accuracy_fraction and macro_f1_fraction use [0,1], Markdown displays percentages.
- Prediction costs are batched validation measurements and include vectorization. They are not interactive latency. One local CPU run supplies no timing confidence interval or hardware-normalized speed ranking.
- Space measurements use MiB (1024 squared bytes). Working sets and lifetime peaks belong to the shared process; earlier algorithms, retained data, garbage collection and native state affect them. They are not isolated per-model RAM and must not be used to rank model memory. Before/after differences are not allocation measurements.
- Neural parameter bytes are actual parameter counts times four for float32, excluding gradients, optimizer state, activations, vocabulary and upstream representation. Classification weights and autoencoder weights were not exported: disk size is N/A. Book artifact sizes describe compressed ML.NET pipelines or private transductive sample JSON, not comparable deployment models; no corpus prose is published.
- One thread is requested in trainer options where exposed, not enforced by process affinity. Framework/native auxiliary workers and operating-system load are not isolated; one-versus-all scoring can parallelize class mappers. Hardware and requested thread settings accompany this report.
- All supervised variants use the same document-component splits, but native, bounded custom and scratch-neural feature representations differ. No independent algorithm-selection holdout or significance test is available.
- Classification native/numeric text features use training-only dictionaries capped at 1024 word unigrams, 1024 word bigrams and 1024 character trigrams per active channel: at most 3 x 1024 x active channels. Blank channels are removed using training input only. Actual dimensions may be smaller; CART further selects bounded training features, while exact KNN uses the complete capped vectors.
- Scratch neural classifiers use a separate training-only vocabulary capped at 2048 tokens including padding/unknown, the first 64 tokens per input and five fixed epochs. These bounded representations differ from the native n-gram channels.
- A runtime-only uncapped pilot was stopped before accuracy inspection because sparse tree fits were expensive. The shared n-gram budget is a fixed resource decision for this completed comparison, not a validation-score optimization.
- MLP, CNN, LSTM, Transformer and autoencoder are scratch TorchSharp C# experiments alongside ML.NET, not pretrained BERT/GPT or a reproduction of the historical paper.
- Linear regression is adapted with separate 0/1 class-indicator targets and argmax selection. It does not regress ordinal label numbers or calendar dates. SVM uses linear margins; the comparison does not cover every variant of each chart family.
- Books have no supervised labels: accuracy and F1 are unavailable. The English within-work passage holdout is not evidence of cross-book generalization.
- KMeans assessment times include both training and heldout cluster summaries. Hierarchical/DBSCAN times describe a bounded training-only sample. Autoencoder prediction time describes heldout numeric reconstruction. These workloads are not directly comparable.
- Book fit figures include the shared text/PCA prerequisites where applicable. Do not sum these per-algorithm figures to estimate total run time, because shared costs are repeated in the presentation.
- Distances and Davies-Bouldin values from different feature spaces are not directly comparable semantic measures. Autoencoder MSE reconstructs normalized PCA vectors and is compared with training-mean and zero-vector baselines.
- Current production classifiers remain separate from these exploratory comparisons; comparison runs do not promote or deploy alternative weights.

Source report SHA256s are preserved in `algorithm-comparison.json`. This summary contains aggregate evidence only.
