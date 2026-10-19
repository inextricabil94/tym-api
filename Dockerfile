FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
WORKDIR /src

COPY Tym.Api.csproj ./
RUN dotnet restore ./Tym.Api.csproj

COPY . ./
RUN dotnet publish ./Tym.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 python3-venv libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY tools/requirements-pos.txt /tmp/requirements-pos.txt
RUN python3 -m venv /opt/tym-pos \
    && /opt/tym-pos/bin/pip install --no-cache-dir -r /tmp/requirements-pos.txt \
    && /opt/tym-pos/bin/python -c "import spacy; [spacy.load(name, exclude=['parser', 'ner', 'lemmatizer', 'senter'])('Adam arrived.') for name in ['en_core_web_sm', 'ro_core_news_sm']]" \
    && rm /tmp/requirements-pos.txt

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    TYM_POS_PYTHON=/opt/tym-pos/bin/python \
    TYM_POS_EN_MODEL=en_core_web_sm \
    TYM_POS_RO_MODEL=ro_core_news_sm
EXPOSE 8080

COPY --from=build /app/publish ./
COPY THIRD_PARTY_NOTICES.md ./
ENTRYPOINT ["dotnet", "Tym.Api.dll"]
