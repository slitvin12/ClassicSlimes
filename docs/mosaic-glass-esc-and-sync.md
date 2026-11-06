# Mosaic glass: «оболочка снимается после Esc» и рассинхрон стекла частей

Найдено и исправлено 06.11.2026. Затронутые файлы: `src/ElasticGlass.cs`, `src/SlimeFacets.cs`.

## Проблема 1: оболочка мозаичного слайма снимается после Esc (и не возвращается)

**Evidence.** В логе игры (`MelonLoader/Logs/26-11-6_0-3-57.log`, строки ~720, 739, 785, 797, 809, 833, 860, 894, 976, 988)
повторяется:

```
[WARNING] [Classic_Slimes] Elastic mosaic glass: ... System.ArgumentNullException: Value cannot be null.
Parameter name: mesh
  at UnityEngine.SkinnedMeshRenderer.BakeMesh (UnityEngine.Mesh mesh, System.Boolean useScale)
  at ClassicSlimes.ElasticGlassRig.Tick(Warp w, Boolean valid)
```

**Корневая причина.** У facet-коата (`SlimeFacets`) цель для `BakeMesh` — поле `Coat.Mesh`
(`Object.Instantiate(original)`). Оно **не** назначается ни на один `MeshFilter` (туда идёт `Shell.Mesh`)
и не имело `hideFlags`. Значит, на него нет нативной ссылки, и `Resources.UnloadUnusedAssets()`
(открытие меню/Esc, загрузка сцены) уничтожает этот «сиротский» меш. Следующий `BakeMesh(null)` бросает
`ArgumentNullException`, а старый обработчик ставил `rig._failed = true` и выключал **все** слои стекла
рига навсегда (`Sync` и `UpdateAll` после этого риг пропускают) — оболочка исчезала и не возвращалась.
Затронуты только facet-коаты (обычные CPU-коаты держат `Mesh` на `MeshFilter`, поэтому живучи) —
отсюда «снимается именно оболочка мозаичного слайма».

**Фикс.**
- `Coat.Bake()` ставит `hideFlags = HideFlags.DontUnloadUnusedAsset` на цель bake (и `SlimeFacets.Shell` — на `Shell.Mesh`):
  `UnloadUnusedAssets` её больше не трогает.
- `Coat.EnsureBakeMesh()` пересоздаёт цель, если её всё же уничтожили, вместо падения.
- Обработчик в `UpdateAll` больше не убивает риг навсегда: прячет коаты, форсирует resync (`_scanAt = 0`)
  и сдаётся только после 5 подряд ошибок (`_errors`), сбрасываясь при успешном кадре.

## Проблема 2: рассинхрон стекла с ушами/хвостами/крыльями

**Evidence.** В том же логе (строки ~1003–1015):

```
Elastic mosaic glass: mesh LF_ringtail_ears_LOD1 (...) is not readable, rigid GPU coat
Elastic mosaic glass: mesh LF_angler_topfin_LOD0 (...) is not readable, rigid GPU coat
... (Flutter wings, Angler bait/sidefins)
```

Части второго родителя (уши/хвост Ringtail, плавники и наживка Angler, крылья Flutter) имеют
`mesh.isReadable == false`, поэтому получали **жёсткий GPU-слой без деформации**, тогда как тело с гранёной
оболочкой сминается и покачивается (`SquashWobbleViaShader`). Отсюда рассинхрон на стыках и в движении
(видно на скриншотах `UserData/CreatorToolkitAgent/screenshots/lt_Mosaic*_A.png`).

**Фикс (вариант, согласован с пользователем: эластичное стекло частей через BakeMesh).**
- `Sync`: нечитаемый, но **skinned** меш пробуется один раз (`CanBake`): `BakeMesh` пишет позу в НАШ читаемый
  меш, поэтому такую часть можно деформировать на CPU. Успех/отказ кешируется по имени меша и пишется в лог
  (`bakes on the CPU - elastic coat (was rigid)` / `cannot be CPU-baked - rigid GPU coat`).
- Нечитаемые skinned части получают CPU-коат: цель bake — свежий `new Mesh()` (копия нечитаемого меша нечитаема),
  `Rest/Normals = null`, `Alpha = empty` (равномерное тонкое стекло 0.012).
- `Coat.Rigid` для них = false (стекло деформируется вместе с телом); читаемые `_NoWobble`-части остаются
  жёсткими, чтобы стекло совпадало с их жёстким мешем.
- Отказ движка запекать → автоматический откат на прежний жёсткий GPU-слой (без регресса).
- Защита первого кадра: если запечённых нормалей ещё нет (`hasN`), смещение по нормали не применяется.

## Как проверить в игре

1. Заспавнить Mosaic-ларго с нужными частями: `spawnslime MosaicFlutter`, `...Ringtail`, `...Angler`, `...Crystal`.
2. В логе искать: `bakes on the CPU - elastic coat (was rigid)` (части теперь эластичные) ИЛИ
   `cannot be CPU-baked - rigid GPU coat` (откат).
3. Проблема 1: открыть/закрыть меню (Esc), посидеть, попрыгать слаймом — оболочка НЕ должна исчезать;
   в логе не должно быть `ArgumentNullException ... BakeMesh`.
4. Dev-команды для состояния: `rinfo` / `mglass`, `slimefacets` (Census показывает facet shells / drawn).

## Откат

Релизная DLL сохранена как `Mods/ClassicSlimes.dll.bak_pre_sync_20261106` (версия 1.3.2, `bf6564bd…`).

## Проверено в игре 06.11.2026 (тестовый сейв, environmentStrand, dev-сборка с этими правками)

- №2: в логе `LF_ringtail_ears_LOD0 / LF_ringtail_tail_LOD0 / LF_angler_bait_LOD0 / LF_angler_topfin_LOD0 /
  LF_angler_sidefins_LOD0 bakes on the CPU - elastic coat (was rigid)` — ближние LOD0 ушей/хвоста/наживки/плавников
  получили эластичные CPU-слои; их `MosaicElasticGlass [MeshRenderer en=True vis=True]`. Дальние LOD1–3 и вторичные
  мешы (`LF_flutterfly_wing_L/R`, `LF_ringtail_*_LOD1..3`) корректно откатились на `rigid GPU coat`.
  Скриншоты `UserData/CreatorToolkitAgent/screenshots/clean_MosaicFlutter.png`, `clean_MosaicRingtail.png`:
  крылья/антенны/уши под стеклом, прилегание без отслоений, гранёная оболочка тела цела.
- №1: за всю сессию (спавны/очистки, позы, открытие-закрытие меню тулкита, freeze mobs, flight, weather,
  cinematic time rate) — **0** `ArgumentNullException ... BakeMesh` и 0 предупреждений `Elastic mosaic glass`
  (старая сборка сыпала ими на тех же триггерах). Facet-оболочка `en=True` до и после цикла открытия/закрытия меню.
- Ограничение автомата: настоящую паузу игры (OS Esc) послать не удалось — Unity InputSystem не принимает
  синтезированный WM_KEYDOWN, а Esc перехватывает Creator Toolkit. Поэтому триггер `UnloadUnusedAssets` паузы
  воспроизводён косвенно (чин-циклы меню/ассетов); сам путь уничтожения меша закрыт `hideFlags`, путь «риг умер
  навсегда» закрыт восстанавливаемым обработчиком, так что симптом «оболочка снялась и не вернулась» устранён
  независимо от точного триггера. Ручная проверка: открыть/закрыть паузу (Esc) на Mosaic-слайме и убедиться, что
  оболочка на месте.
