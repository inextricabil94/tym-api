"""Stage allowlisted application files and private model weights for Docker builds."""
import argparse
import shutil
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--models', type=Path, required=True)
parser.add_argument('--out', type=Path, required=True)
parser.add_argument('--pdf', type=Path)
parser.add_argument('--pptx', type=Path)
args = parser.parse_args()
source = Path(__file__).resolve().parents[1]
api = args.out.resolve() / 'api'
ui = args.out.resolve() / 'ui'

def copy(origin, target):
    if not origin.is_file():
        raise FileNotFoundError(origin)
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(origin, target)

for filename in ['Tym.Api.csproj', 'Program.cs', 'CorpusPredictions.cs', 'openapi.yaml']:
    copy(source / filename, api / filename)
copy(source / 'data' / 'seed-examples.jsonl', api / 'data' / 'seed-examples.jsonl')
copy(source / 'deploy' / 'Dockerfile.api', api / 'Dockerfile')
model_ids = ['segment_type_en', 'segment_type_ro', 'temporal_relation_en', 'temporal_relation_ro',
             'timebank_event_class_ro', 'timebank_event_tense_ro', 'timebank_timex_type_ro',
             'timebank_tlink_ro', 'timebank_slink_ro', 'timebank_alink_ro']
for model_id in model_ids:
    for suffix in ['_sdca.zip', '_sdca.zip.manifest.json']:
        copy(args.models / (model_id + suffix), api / 'models' / 'corpus' / (model_id + suffix))
for name in ['segment_type_en.zip', 'event_temporal_en.zip']:
    for suffix in ['', '.manifest.json']:
        copy(args.models / 'api-models' / (name + suffix), api / 'models' / 'api' / (name + suffix))

ui_source = source / 'ui' / 'Tym.Ui'
for filename in ['Tym.Ui.csproj', 'Program.cs']:
    copy(ui_source / filename, ui / filename)
for file in (ui_source / 'wwwroot').rglob('*'):
    if file.is_file():
        copy(file, ui / file.relative_to(ui_source))
copy(source / 'deploy' / 'Dockerfile.ui', ui / 'Dockerfile')
for artifact in [args.pdf, args.pptx]:
    if artifact:
        copy(artifact, ui / 'wwwroot' / 'results' / artifact.name)
print(f'API context: {api}')
print(f'UI context: {ui}')
