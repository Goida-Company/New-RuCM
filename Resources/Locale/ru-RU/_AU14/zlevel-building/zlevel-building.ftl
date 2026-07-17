# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 wray-git
# SPDX-License-Identifier: AGPL-3.0-only
# Building overhaul (z-level) - Phase 1: structural support graph
au-zsupport-unsupported = Эта секция больше не имеет опоры!
au-zsupport-admin-alert = Структура на Z-уровне обрушилась (потеря опоры) - вероятная причина: { $culprit }.

# Building overhaul (z-level) - underground cave-ins
au-cavein-warning = Потолок здесь стонет и трещит - сейчас будет обвал!
au-cavein-admin-alert = Подземный обвал ({ $count } тайлов) - вероятная причина: { $culprit }.

# Building overhaul (z-level) - structural scanner
au-scanner-on = Вы включаете структурный сканер.
au-scanner-off = Вы выключаете структурный сканер.

# Building overhaul (z-level) - mapper opt-out condition
construction-step-condition-au14-zbuild-allowed = На этой карте должно быть разрешено вертикальное строительство.

# Building overhaul (z-level) - construction menu entries
au14-construction-tile-plating = настил
au14-construction-tile-plating-desc = Уложить металлический настил. Можно размещать над пустотой, чтобы строить полы в воздухе.
au14-construction-tile-steel = стальной пол
au14-construction-tile-steel-desc = Уложить стальной пол. Можно размещать над пустотой, чтобы строить полы в воздухе.
au14-construction-tile-dirt = земля
au14-construction-tile-dirt-desc = Насыпать участок земли. Можно размещать над пустотой, чтобы строить полы в воздухе.

au14-construction-z-stairs-up = лестница z-уровней (вверх)
au14-construction-z-stairs-up-desc = Лестница, ведущая на один z-уровень выше; создает стоячую площадку уровнем выше и ставит здесь опорную балку.
au14-construction-z-stairs-down = лестница z-уровней (вниз)
au14-construction-z-stairs-down-desc = Лестница, ведущая на один z-уровень ниже; отражает опорную балку на уровне ниже.

au14-construction-support-beam-wood = деревянная опорная балка
au14-construction-support-beam-wood-desc = Деревянная опорная балка. Ставьте ее под полом верхнего уровня, чтобы удерживать его: дешево, но перекрывает малое расстояние.
au14-construction-support-beam-metal = металлическая опорная балка
au14-construction-support-beam-metal-desc = Стальная опорная балка. Ставьте ее под полом верхнего уровня, чтобы удерживать его: надежный универсальный пролет.
au14-construction-support-beam-plasteel = пласталевая опорная балка
au14-construction-support-beam-plasteel-desc = Пласталевая опорная балка. Ставьте ее под полом верхнего уровня, чтобы удерживать его: дорого, зато перекрывает самую широкую платформу.

## Z-Level Toggles admin tool (construction menu > Tools)
au-zlevel-toggles-title = Переключатели Z-уровней
au-zlevel-toggles-search = Поиск карт...
au-zlevel-toggles-hint = Да = игроки могут строить по Z-уровням на этой карте. Сохраняется между раундами.
au-zlevel-toggles-yes = Да
au-zlevel-toggles-no = Нет
au-zlevel-toggles-map-loaded = {$map} (загружена)
au-zlevel-toggle-enabled = Строительство по Z-уровням РАЗРЕШЕНО на {$map}.
au-zlevel-toggle-disabled = Строительство по Z-уровням ЗАПРЕЩЕНО на {$map}.

## Отладочные и административные команды
cmd-au-zsupport-desc = Пересчитывает граф опор Z-уровней и выводит число поддерживаемых и неподдерживаемых конструкций.
cmd-au-zsupport-help = Использование: au_zsupport [all]
cmd-au-zsupport-recomputed-all = Пересчитано сеток: { $grids }.
cmd-au-zsupport-player-only = Выполните команду в игре либо используйте «au_zsupport all».
cmd-au-zsupport-not-on-grid = Вы не стоите на сетке. Попробуйте «au_zsupport all».
cmd-au-zsupport-recomputed-grid = Пересчитана ваша сетка { $grid }.
cmd-au-zsupport-report = { $prefix } Опоры: поддерживается — { $supported }, не поддерживается — { $unsupported }.

cmd-au-dig-player-only = Эту команду может выполнить только игрок в игре.
cmd-au-digup-desc = Прокапывает проход строго на один Z-уровень вверх в текущей горизонтальной позиции.
cmd-au-digup-help = Использование: au_digup
cmd-au-digup-success = Прокопан проход на уровень вверх.
cmd-au-digup-failed = Здесь нельзя прокопать проход вверх: над вами нет уровня, место перекрыто стеной или функция отключена.
cmd-au-digdown-desc = Прокапывает проход вниз, создавая или открывая каменный Z-уровень под вами.
cmd-au-digdown-help = Использование: au_digdown
cmd-au-digdown-success = Прокопан проход на уровень вниз.
cmd-au-digdown-failed = Здесь нельзя прокопать проход вниз: карта запретила это, функция отключена или ниже уже есть созданный вручную уровень.

cmd-au-multiz-desc = Показывает состояние вертикального строительства AU14 на картах и переключает его для карты или глобально.
cmd-au-multiz-help = au_multiz (список) | au_multiz <MapId> <on|off> | au_multiz global <on|off>
cmd-au-multiz-enabled = ВКЛЮЧЕНО
cmd-au-multiz-disabled = ВЫКЛЮЧЕНО
cmd-au-multiz-yes = Да
cmd-au-multiz-no = Нет
cmd-au-multiz-global-status = Глобальное строительство по Z-уровням AU14: { $state } (переключение: au_multiz global on|off)
cmd-au-multiz-map-status = MapId { $id } { $map } — несколько Z-уровней: { $state }
cmd-au-multiz-usage = Использование: au_multiz <MapId|global> <on|off>
cmd-au-multiz-invalid-state = Второй аргумент должен быть «on» или «off».
cmd-au-multiz-global-changed = Глобальное строительство по Z-уровням AU14: { $state }.
cmd-au-multiz-invalid-map = Аргумент карты должен быть числовым MapId (список: «au_multiz») или «global».
cmd-au-multiz-map-not-found = Карта с MapId { $id } не найдена.
cmd-au-multiz-can-build = теперь могут
cmd-au-multiz-cannot-build = больше не могут
cmd-au-multiz-map-changed = Для карты { $id } несколько Z-уровней: { $state }. Игроки { $permission } строить здесь лестницы и полы по Z-уровням AU14.
