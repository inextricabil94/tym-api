FROM node:24-alpine AS typescript
WORKDIR /ui
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts
COPY tsconfig.json ./
COPY client ./client
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Tym.Corpus.Ui.csproj ./
RUN dotnet restore Tym.Corpus.Ui.csproj
COPY . ./
COPY --from=typescript /ui/wwwroot/ ./wwwroot/
RUN dotnet publish Tym.Corpus.Ui.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false /p:SkipTypeScriptBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Tym.Corpus.Ui.dll"]
