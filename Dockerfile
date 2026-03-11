FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
COPY *.csproj ./
RUN dotnet restore

# Копируем исходный код и собираем проект
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Финальный образ
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Устанавливаем часовой пояс
ENV TZ=Europe/Moscow
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

ENTRYPOINT ["dotnet", "TgDataPlanner.dll"]