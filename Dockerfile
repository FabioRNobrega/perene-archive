FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends ffmpeg openssl \
    && rm -rf /var/lib/apt/lists/*

RUN dotnet tool install --global dotnet-ef --version 10.0.11

WORKDIR /workspace

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1
ENV PATH="${PATH}:/root/.dotnet/tools"

EXPOSE 8080

CMD ["sh", "-lc", "cd WebApp/WebApp && exec dotnet watch run --no-launch-profile --urls=http://0.0.0.0:8080"]
