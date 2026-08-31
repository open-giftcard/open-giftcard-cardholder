# Build and run the cardholder application. The same image applies the
# cardholder's session-store migrations, selected by the --migrate argument, so
# the schema is never applied by a build that differs from the one that will
# serve it. This mirrors the backend image deliberately.
#
# There is no front-end build step. The cardholder is server-rendered Razor
# Pages and ships no JavaScript bundle by design, so the published output is
# the whole application.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props GiftCardCardholder.slnx ./
COPY src/GiftCardCardholder.Web/ src/GiftCardCardholder.Web/
RUN dotnet restore src/GiftCardCardholder.Web/GiftCardCardholder.Web.csproj

RUN dotnet publish src/GiftCardCardholder.Web/GiftCardCardholder.Web.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# libgssapi-krb5-2 for Npgsql's connection negotiation, curl for the
# healthcheck. Neither ships in the runtime image.
RUN apt-get update \
    && apt-get install --no-install-recommends --yes libgssapi-krb5-2 curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

RUN mkdir -p /var/lib/open-giftcard-cardholder/dataprotection-keys \
    && chown -R $APP_UID /var/lib/open-giftcard-cardholder

USER $APP_UID

EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

ENTRYPOINT ["dotnet", "GiftCardCardholder.Web.dll"]
