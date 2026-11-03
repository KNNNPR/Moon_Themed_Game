# Режимы изображения

В проекте доступны **Retro** (cel-shading + dithering из PunchCard0) и **Color ASCII**.
По умолчанию включён Color ASCII. Цвет символов берётся из сцены, яркость выбирает
один из 16 символов. Неподвижный рисунок не мерцает и не прокручивается.

## Переключение

- В игре / Play Mode: **F6**, когда окно Game получает ввод с клавиатуры.
- В редакторе: **Tools → Moon Game → Visual Style → Color ASCII** или **Retro Dither + Cel**.
- Начальный режим: `Assets/Resources/VisualStyleSettings.asset` → **Default Style**.
  Выбор через меню в Edit Mode также сохраняет начальный режим.
  Переключение в Play Mode действует только в текущем сеансе.

Оба режима работают в PC и Mobile URP. Каждый кадр исполняется только один
экранный эффект; ASCII получает изображение до dithering. Cel-материалы и свет
сцены используются в обоих режимах. Намеренно пустые слоты модели сохранены.

## Настройка ASCII

**Tools → Moon Game → Visual Style → Select ASCII Material** открывает
`Assets/Materials/Retro/ColorAscii.mat`.

| Параметр | Результат |
| --- | --- |
| Character Height | Размер символов в пикселях: 6 — плотнее, 8 — по умолчанию, 12–16 — крупнее. |
| Character Brightness | Яркость цветных символов. |
| Scene Behind Characters | Сколько исходного изображения видно между символами; 0 — чёрный фон. |
| Local Color Detail | Сохранение мелких цветных деталей внутри одной ячейки. |
| Color Saturation | 1 сохраняет цвета; 0 делает изображение монохромным. |
| Character Density | Насколько быстро появляются более заполненные символы. |
| Empty Dark Cells | Порог, ниже которого символы в тенях скрыты. |
| Effect Intensity | 0 полностью отключает ASCII, 1 даёт полный эффект. |

Для меню настроек можно вызвать `VisualStyleSwitcher.SetStyle(VisualStyle.ColorAscii)`,
`VisualStyleSwitcher.SetStyle(VisualStyle.Retro)` или `VisualStyleSwitcher.ToggleStyle()`
из пространства имён `MoonThemedGame.Rendering`.
Методы компонента `UseColorAscii`, `UseRetro` и `Toggle` также подходят для UI Button.

Глобальный переключатель F6 создаётся автоматически при старте игры. Для мобильного
управления эти же методы можно подключить к кнопкам меню.

**Install or Repair** в меню Visual Style восстанавливает подключение к обоим
рендерам, сохраняя значения материалов и выбранный начальный режим.
