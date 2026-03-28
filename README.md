# FlowShellBar

`FlowShellBar` — это отдельное shell-приложение в составе `FlowShell`, отвечающее за верхний bar сессии.

Он показывает текущее shell-state, предоставляет быстрые действия пользователя и может работать как самостоятельно, так и в интеграции с `FlowShellCore`.

Канонический источник истины находится в `docs/dev` и `docs/meta`.

## Current Baseline

Текущий runtime skeleton реализован на `C# + WinUI 3 + XAML`.

- solution: `FlowShellBar.slnx`
- app project: `src/FlowShellBar.App`
- mode: `standalone`
- data source: mock bar model

## Build

```powershell
dotnet build FlowShellBar.slnx
```

## Run

```powershell
dotnet run --project src\FlowShellBar.App\FlowShellBar.App.csproj
```
