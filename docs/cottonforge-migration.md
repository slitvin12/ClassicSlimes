# Перенос ClassicSlimes на CottonForge

Ветка `cottonforge-migration` (от `v1.1.0`, `dd7c1d3`). Здесь — план; код мода в ветке не менялся, добавлено только
свойство сборки `CottonDll`.

## Цель

ClassicSlimes стоит на CottonLibrary 0.3.1 и сам несёт много общей инфраструктуры: обходы багов Cotton и игры,
фабрику слаймов, процедурную графику, dev-консоль, диагностику. CottonForge — наш форк CottonLibrary, куда эта
инфраструктура переезжает. После переноса в ClassicSlimes остаётся SR1-контент: пять слаймов (Rad, Quantum, Mosaic,
Quicksilver, Glitch), рецепты их внешнего вида, механики, ресурсы, тексты и настройки.

## Пока форк «допекается»

- `D:\SR2ModDev\CottonForge` сейчас пишет другой агент (архитектура, архетипы SR1/SR2, рынок плортов, дроны,
  гаджеты, URP-шейдеры). Отсюда — только чтение: не править, не собирать (общие `obj\` и `bin\`), git — только
  `git --no-optional-locks status/log/diff`.
- Сборка ClassicSlimes против форка — по копии DLL вне его дерева: `%TEMP%\cf_snapshot\CottonLibrary.dll` (копия
  готовой `obj\Release\net6.0\CottonLibrary.dll`; обновлять, когда тот агент соберёт новую).
- Чего нет в форке — не дописываем сами, а передаём списком (этап 4).
- `main` остаётся на Cotton 0.3.1, пока перенос не проверен в игре.

## Снимок форка (02.11.2026 00:44)

| | |
|---|---|
| Ветка | `forge` @ `4c040c4` (тег upstream `v0.3.0`) + 47 записей в `git status`; `Forge\`, `docs\`, `examples\`, `README.md` — новые |
| Сборка | `CottonLibrary`, версия 0.3.2 («fork of 0.3.1»); DLL 496 640 Б от 02.11 00:32 |
| API | публичный API Cotton 0.3.1 сохранён (drop-in замена); поверх — слой `CottonLibrary.Forge`: 59 файлов, 12 модулей |
| Репозиторий | только remote `upstream` (GitLab `thean-mcgarity/CottonLibrary`); своего репозитория и файла лицензии нет |
| Статус | собран, в игре не проверен; URP-шейдеров в дереве пока нет |

Модули `Forge`:

- Core — `ForgeLog`/`Safe`, `Scheduler`, `ForgeSave` (JSON в `UserData\CottonLibrary\saves`), `LiveConfig`, `Gc`,
  `ModAssets` (меши CSM1 из наших `tools`), `Il2CppExt`, `Compat`;
- Fixes — `GameFixes`: `SlimeIndexDedup`, `ContentGenerationWindow` (пауза GC), `FirstGameLoad`, `RadiantPushDiag`;
- Content — `ForgeMod<M>`, `SlimeBuilder`, `Idents`, `Largos`, `Foods`, `Spawns`, `Loc`, `Behaviours`;
- Visual — `Gfx`, `Mat`/`MatTools`, `MaterialSwap`, `MeshKit`, `Faces`/`FaceStyles`, `Looks`, `PediaArt`, `FxSnd`,
  `SoundBank`, `Voices`, `Presets`;
- Gameplay — `World`/`WorldExtra`, `SlimeActor`, `SlimeEvents` (с `Movement.TakeOver`), `Aura`, `CorralWalls`, `RadHud`;
- Architecture (`Contracts`, `ForgeModules`, `ActorRuntime` — логика без IL2CPP-инъекции), Anatomy (архетипы SR1 и
  SR2, `LargoEngine`, `DietTools`), Environment, Science (гаджеты, дроны), Economy (`PlortMarket`, `PriceModels`),
  Player (`VacPack`, `ForgeHud`), Dev (`DevConsole`, `DevTools`, `Diag`, `EngineCommands`).

В README форка уже есть раздел «Перенос ClassicSlimes»; этот план с ним согласован и добавляет порядок, проверки и
риски.

## Что ClassicSlimes берёт у Cotton

`Library.CreateSlimeDef`, `CreatePlortType`, `CreatePrefab`, `CreatePediaEntryForSlime`, `CreateLocalizedString`,
`AddLanguages`, `GetSlime`, `GetPlort`, `SetPlortColor`, `MakeVaccable`, `GetSceneNamesFromSpawnerZones`,
`LargoSettings`, `SpawnLocations`, списки `fruits`/`veggies`/`plorts`/`food`, а через рефлексию —
`Library.INTERNAL_GetSloomberPalette` (`SloomberPaletteGuard`). В форке всё это есть: сборка против снимка —
0 ошибок, 0 предупреждений; `INTERNAL_GetSloomberPalette` на месте и уже исправлен внутри форка.

## Сборка против форка

```
cd src
dotnet build -c Release -p:CottonDll="%TEMP%\cf_snapshot\CottonLibrary.dll"
```

По умолчанию `CottonDll` = `$(SR2Dir)\Mods\CottonLibrary.dll`. Свойство влияет только на компиляцию: в игре
загружается та `CottonLibrary.dll`, что лежит в `Mods`. Форк заменяет Cotton под тем же именем, поэтому для теста в
игре его DLL кладут вместо Cotton 0.3.1 — это правка чужого мода: только с согласия пользователя, с `.bak` и
`nai_savebak.py`.

## Этапы

### 0. Подготовка — сделано

Ветка, этот план, свойство `CottonDll`. Собрано по умолчанию и против снимка форка — 0 ошибок, 0 предупреждений;
неверный путь в `CottonDll` даёт ошибки компиляции, то есть свойство работает. В игре не проверялось: поведение мода
не менялось.

### 1. Drop-in: ClassicSlimes 1.1.0 без изменений на DLL форка

Когда тот агент скажет, что форк готов к тесту:

1. `nai_savebak.py`; `Mods\CottonLibrary.dll` → `.bak`; DLL форка — в `Mods`.
2. Три запуска (`crashloop.py`) без падений; оба лога (`nai_log.py`); `nai_dupe.py` — счётчики `same key` и
   `_SloomberMiddleColor` не выросли.
3. Старый сейв: пять слаймов, их ларго, плорты в силосах и на рынке, записи слаймопедии на месте. Новый сейв —
   спавн по зонам.
4. Двойные патчи — их ставят и ClassicSlimes, и форк: `Object.Destroy`/`DestroyImmediate` (`DestroyGuard` у обоих),
   `CalmedByWaterSpray.AddLiquid`, `DirectedActorSpawner.Awake`/`CanSpawnSomething`/`Spawn`,
   `DirectedSlimeSpawner.OnActorSpawned` (у нас — только в dev-режиме), `GotoConsumable.Action`,
   `PediaEntryIconAndHighlights.SetPediaEntry`, `RadiantSlimesModel.Push`, `SlimeDefinitions.RefreshIndexes`,
   `SlimeEat.Awake`, `SlimeEat.InvokeOnEat`, `SlimeRandomMove.Action`, `SpawnResource.Awake`. Ожидаем, что без
   Forge-модов патчи форка ничего не делают; проверить воду на Rad и Quantum, еду, бег Quicksilver без прыжков,
   слаймопедию, спавн. Двойная пауза GC (форк — в `AutoSaveDirector.Awake`, мы — в `SaveDirectorLoading`)
   безвредна: `il2cpp_gc_disable` считает вложенность.
5. Патчи только форка: `AutoSaveDirector.Awake`, `Gadget.Start`, `GameModelPull/PushHelpers`,
   `LightningStrike.Start`, `PlortEconomyDirector.GetTargetValue`/`RegisterSold`, `VacuumItem.Expel`. Проверить
   цены наших плортов, выстрел слаймами и плортами из пылесоса (бегство Glitch после броска зависит от состояния
   `Vacuumable`), нет ли новых файлов в `UserData\CottonLibrary\saves`.
6. Dev-режим ClassicSlimes (`UserData\ClassicSlimes\cmd.txt`) работает; консоль форка по умолчанию выключена
   (`DevConsole=false`).

Итог — решение, делать ли форк обязательной зависимостью (ClassicSlimes 2.0).

### 2. Инфраструктура — на API форка

По одному блоку; после каждого — сборка, тест в игре, коммит. От безопасного к рискованному:

1. Обходы: убрать `SlimeIndexDedup`, `SloomberPaletteGuard`, `DestroyGuard` (`Hooks.cs`; свои материалы лиц —
   через `Faces.Protect`), `SaveGuard.cs` (→ `RadiantPushDiag`), свою паузу GC (→ `ContentGenerationWindow`).
2. Утилиты `Util.cs`: `Gc`, `Mat`, `Fx`/`Snd`, `Dmg`, `U` → `Gc`, `Mat`/`MatTools`, `FxSnd`, `World` — после того
   как в форк попадёт исправленный `Grounded` (этап 4).
3. Контент: `Main.cs` → `ForgeMod<M>`; `SlimeFactory.cs` → `SlimeBuilder` (те же ID, см. «Риски»); `Spawns.cs`,
   `Foods.cs` (`EnableHidden`/`AddToWild`), `Loc.cs`, `Pedia.cs` → `PediaArt`, `Res.cs` → `ModAssets`,
   `RadHud.cs` → `RadHud`.
4. Графика: `Gfx.cs`, `Faces.cs` (→ `Faces`, `FaceStyles.Glow`/`Hidden`), общие помощники `Looks.cs` и
   `Visuals.cs` → `Looks`/`Mat`/`Presets`; `Swirl.cs` → `MeshKit.BakeSurface`; оболочки из `Experiments.cs` →
   `MeshKit.AddLayer`.
5. Dev: общие команды `DevCmd.cs` → `DevConsole`/`DevTools`, `Diag.cs`/`DiagX.cs` → `Diag`; команды слаймов —
   через `DevConsole.Register`. Тогда же — пути в `nai_c.py`/`nai_t.py`.

### 3. Механики

`Behaviours.cs` — 1810 строк, 9 инжектированных IL2CPP-типов (`RadSlime`, `QuantumSlime`, `QuantumGhost`,
`MosaicSlime`, `MosaicGlint`, `QuicksilverSlime`, `GlitchSlime`, `Billboard`, `SphereBillboard`).

1. Сначала — на примитивы форка без смены логики: `_rb`/`_vac`/`_emo` → `SlimeActor`, подмена материалов (`Swap`)
   → `MaterialSwap`, стены загона Quantum → `CorralWalls`, облучение Rad → `Aura.Irradiate`, телепорт →
   `World.Blink`, вода, еда и прыжки → `SlimeEvents`/`Movement.TakeOver`, `Cfg` → `LiveConfig.Watch`,
   SR2MP → `Compat.Multiplayer`.
2. Потом — решение по архетипам: в форке пишутся SR1-архетипы (quantum, dervish, tangle, mosaic, glitch, rad,
   quicksilver). Либо ClassicSlimes настраивает архетипы форка и держит только параметры и визуал, либо механики
   остаются здесь. Решать, когда архетипы будут готовы и проверены в игре; критерий — паритет с v1.1.0 (ниже).
   Тогда же — уходить ли от инжектированных типов к `ActorRuntime`.

Паритет v1.1.0 (проверено в игре): Mosaic — желе-риг, стеклянная оболочка качается вместе с плитками; Rad — дышащая
аура размера SR1 и марево; Quantum — прозрачное стеклянное желе с объёмной спиралью внутри; Glitch убегает, если его
поймали (маскировка раскрыта) и бросили; Quicksilver бегает по земле и не прыгает. Плюс всё из README → «Что
добавлено».

### 4. Передать в CottonForge

Чего в форке нет на 02.11.2026 00:44. Отдать тому агенту списком после его текущей волны, не патчить отсюда:

1. `World.Grounded` — старый луч (0,35 вверх, 0,75 вниз) не достаёт до земли: корень слайма висит примерно на
   полтела выше. Исправление из ClassicSlimes (`Util.cs`, проверено в игре):
   ```csharp
   Physics.Raycast(t.position + Vector3.up * (0.1f * scale), Vector3.down, 0.85f * scale, GroundMask, QueryTriggerInteraction.Ignore);
   ```
   От него зависят `SlimeActor.Grounded` и всё, что на нём построено.
2. `JellyRig` (`Jelly.cs`) — пружинящее желе для слаймов со стеклянной оболочкой; кандидат в `Visual`.
3. `Spiral3D` (`Experiments.cs`) — объёмная спираль-трубка Quantum; кандидат в `MeshKit`.
4. Glitch: бегство после поимки и броска (`GlitchFlees`) — опция архетипа glitch.
5. Quicksilver «бегает, не прыгает» (`QsNoHopMove`, `QsNoHopFood`) — сверить с `Movement.TakeOver`.
6. Dev-команды `gvac`, `mjelly`, `wobble`; `track` с контактом с землёй, `kin` и расстояниями до игрока и камеры.
7. Грабли: `Vacuumable.SetHeld(true)` без сустава пылесоса — слой HELD проваливается сквозь землю (держать
   `isKinematic`); `pose` делает слайма кинематическим на всё время позы — для проверки физики не годится.

Рецепты (дышащая аура и марево Rad, стеклянное желе Quantum) остаются контентом ClassicSlimes; в форк — только если
из них выйдут общие пресеты `Presets`.

### 5. Выпуск

ClassicSlimes 2.0 с зависимостью от CottonForge: README (установка форка вместо Cotton), релиз на GitHub — после
публикации форка.

## Карта модулей

| Файл | Строк | В CottonForge | Этап | Остаётся в ClassicSlimes |
|---|---:|---|---|---|
| `Main.cs` | 95 | `ForgeMod<M>` | 2.3 | регистрация мода |
| `Cfg.cs` | 74 | `LiveConfig.Watch` | 3.1 | все настройки (`[ClassicSlimes]`, имена те же) |
| `SlimeFactory.cs` | 366 | `SlimeBuilder`, `Largos` | 2.3 | спецификации пяти слаймов, диеты |
| `Spawns.cs` | 150 | `Spawns` | 2.3 | зоны и веса |
| `Foods.cs` | 181 | `Foods.EnableHidden`/`AddToWild` | 2.3 | список еды SR1 |
| `Loc.cs` | 60 | `Loc` | 2.3 | `localized.csv` |
| `Res.cs` | 175 | `ModAssets` | 2.3 | — |
| `Pedia.cs` | 101 | `PediaArt` | 2.3 | арт слаймопедии |
| `RadHud.cs` | 84 | `RadHud` | 2.3 | — |
| `SaveGuard.cs` | 47 | `GameFixes` (`RadiantPushDiag`) | 2.1 | — |
| `Hooks.cs` | 230 | `GameFixes`, `Faces.Protect`, `SlimeEvents`, `Movement`, `DevTools` (галерея) | 2.1, 2.5, 3.1 | — |
| `Util.cs` | 566 | `Gc`, `Mat`, `FxSnd`, `World` | 2.2 | — |
| `Gfx.cs` | 493 | `Gfx` | 2.4 | — |
| `Faces.cs` | 222 | `Faces`, `FaceStyles` | 2.4 | — |
| `Looks.cs` | 748 | `Looks`, `Mat`, `Presets` | 2.4 | рецепты Rad, Mosaic, Glitch |
| `Visuals.cs` | 554 | `Mat`, `Presets` | 2.4 | материалы SR1 |
| `Jelly.cs` | 142 | новое (`Visual`) | 4 | до переноса — здесь |
| `Swirl.cs` | 197 | `MeshKit.BakeSurface` | 2.4 | рисунок спирали |
| `Experiments.cs` | 261 | `MeshKit.AddLayer`; `Spiral3D` — новое | 2.4, 4 | — |
| `Behaviours.cs` | 1810 | `SlimeActor`, `MaterialSwap`, `CorralWalls`, `Aura`, `World.Blink`; архетипы — по решению | 3 | механики, пока нет решения по архетипам |
| `DevCmd.cs` | 900 | `DevConsole`, `DevTools` | 2.5 | `qsp`, `qspot`, `qspiral`, `gflee`, `gvac`, `gblink`, `gburst`, `gmask`, `gtrail`, `mjelly`, `rdist`, `qnophase` |
| `Diag.cs`, `DiagX.cs` | 382 + 178 | `Diag` | 2.5 | — |
| `Assets/` | 29 png, 19 wav, 6 csm, 1 csv (4,5 МБ) | — | — | всё |

## Риски

- ID в сейвах. `SlimeBuilder` обязан давать те же ID, что Cotton 0.3.1: `SlimeDefinition.<Name>`,
  `IdentifiableType.<Name>Plort`, `<Name>Default`, префаб `plort<Name>`, — иначе слаймы и плорты пропадут из старых
  сейвов. Сверять на старом сейве до и после.
- Ларго. Имена и ID ларго генерирует Cotton (`LargoSettings`); `LargoEngine` форка должен давать те же.
- Настройки. Категория `[ClassicSlimes]` и имена настроек при переходе на `LiveConfig` не меняются.
- Двойные патчи на этапах 1–2 (список в этапе 1): двойные эффекты — два обработчика воды или еды, двойной спавн.
- Состояние вне сейва. `ForgeSave` пишет JSON в `UserData\CottonLibrary\saves`; наше состояние туда без нужды не
  переносить.
- Dev-инструменты. `nai_c.py`/`nai_t.py` пишут в `UserData\ClassicSlimes\cmd.txt`; консоль форка —
  `UserData\CottonLibrary\cmd.txt`, по умолчанию выключена.
- Чужие моды. Форк заменяет Cotton всем модам пользователя, которые от него зависят: drop-in проверить и на них.
- Распространение. Игроку придётся ставить CottonForge вместо Cotton; у форка нет ни репозитория, ни лицензии (в
  дереве upstream нет `LICENSE`) — решить до выпуска 2.0.
- SR2MP. `Compat.Multiplayer` проверить, если играем с SR2MP.

## Открытые вопросы

1. Где и под каким именем публиковать форк, с какой лицензией; как ClassicSlimes будет на него ссылаться.
2. Механики SR1: канон в архетипах форка или в ClassicSlimes.
3. ClassicSlimes 2.0 на форке — поддерживать ли 1.x на Cotton 0.3.1.
4. Пушить ли эту ветку на GitHub до публикации форка (репозиторий ClassicSlimes публичный).
