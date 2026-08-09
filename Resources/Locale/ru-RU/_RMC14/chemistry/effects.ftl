reagent-effect-guidebook-rmc-antitoxic =
    Лечит [color=green]{$healing}[/color] токсинов и удаляет [color=green]0.125[/color] ед. токсичных веществ из кровотока.
    Критическая передозировка вызывает потерю сознания на [color=red]5[/color] секунд с шансом [color=red]5%[/color]

reagent-effect-guidebook-rmc-biocidic =
    Наносит [color=red]{$damage}[/color] ед. травм.
    Передозировка наносит [color=red]{$overdose}[/color] ед. травм.
    Критическая передозировка наносит [color=red]{$critical}[/color] ед. травм

reagent-effect-guidebook-rmc-carcinogenic =
    Наносит [color=red]{$genetic}[/color] ед. генетического урона.
    Передозировка наносит [color=red]{$overdose}[/color] ед. генетического урона.
    Критическая передозировка наносит [color=red]{$critical}[/color] ед. травмы

reagent-effect-guidebook-rmc-alchemist-pain = Увеличивает боль на [color=red]{$amount}[/color] в секунду

reagent-effect-guidebook-rmc-alchemist-purge = Удаляет [color=red]{$amount}[/color] ед. подходящих нетоксичных химикатов в секунду

reagent-effect-guidebook-rmc-ketogenic =
    Удаляет [color=red]{$nutrients}[/color] ед. питательных веществ, вызывая голод со временем.
    Увеличивает скорость метаболизма алкоголя на [color=green]{$alcohol}[/color] ед.
    Передозировка вызывает потерю [color=red]{$odNutrition}[/color] ед. питания, [color=red]{$odToxin}[/color] ед. токсинов и [color=red]{$odChance}%[/color] шанс рвоты.
    Критическая передозировка вызывает потерю сознания на [color=red]10[/color] секунд

reagent-effect-guidebook-rmc-thermostabilizing =
    Стабилизирует температуру тела до [color=green]{$target}[/color] кельвинов, изменяя её на [color=green]{$step}[/color] К за раз.
    Передозировка вызывает потерю сознания на [color=red]10[/color] секунд.
    Критическая передозировка вызывает потерю сознания на [color=red]5[/color] секунд с шансом [color=red]5%[/color]

reagent-effect-guidebook-rmc-electrogenetic =
    Лечит [color=green]{$heal}[/color] ед. травм, ожогов и токсинов при дефибрилляции.
    Удаляет 1 ед. этого вещества из раствора при дефибрилляции

reagent-effect-guidebook-rmc-corrosive =
    Наносит [color=red]{$damage}[/color] ед. ожогов.
    Передозировка наносит [color=red]{$overdose}[/color] ед. ожогов.
    Критическая передозировка наносит [color=red]{$critical}[/color] ед. ожогов

reagent-effect-guidebook-rmc-hypoxemic =
    Наносит [color=red]{$airloss}[/color] ед. урона от удушья и заставляет жертву хватать ртом воздух.
    Передозировка наносит [color=red]{$odBrute}[/color] ед. травмы, [color=red]{$odToxin}[/color] ед. токсинов и [color=red]{$odAirloss}[/color] ед. урона от удушья.
    Критическая передозировка наносит [color=red]{$critBrute}[/color] ед. травмы и [color=red]{$critToxin}[/color] ед. токсинов

reagent-effect-guidebook-rmc-toxic =
    Наносит [color=red]{$damage}[/color] ед. токсинов.
    Передозировка наносит [color=red]{$overdose}[/color] ед. токсинов.
    Критическая передозировка наносит [color=red]{$critical}[/color] ед. токсинов

reagent-effect-guidebook-rmc-antihallucinogenic =
    Удаляет [color=green]2.5[/color] ед. токсина разрушителя разума и космических наркотиков из кровотока. Также стабилизирует нарушения восприятия, такие как галлюцинации.
    Передозировка наносит [color=red]{$odToxin}[/color] ед. токсинов.
    Критическая передозировка наносит [color=red]{$critBrute}[/color] ед. травмы, [color=red]{$critBurn}[/color] ед. ожогов и [color=red]{$critToxin}[/color] ед. токсинов

reagent-effect-guidebook-rmc-focusing =
    Удаляет [color=green]{$alcohol}[/color] ед. алкоголя и [color=green]{$drunk}[/color] секунд опьянения{$powerful ->
        [true] . Также достаточно мощное, чтобы мгновенно вылечить немоту и слепоту.
       *[false] .
    }
    Передозировка наносит [color=red]{$odToxin}[/color] ед. токсинов.
    Критическая передозировка наносит [color=red]{$critToxin}[/color] ед. токсинов

reagent-effect-guidebook-rmc-nutritious = Восстанавливает [color=green]{$amount}[/color] ед. питательных веществ организму и утоляет голод

reagent-effect-guidebook-rmc-anticarcinogenic =
    Лечит [color=green]{$heal}[/color] ед. генетического урона.
    Передозировка наносит [color=red]{$overdose}[/color] ед. токсинов.
    Критическая передозировка наносит [color=red]{$critical}[/color] ед. травмы

reagent-effect-guidebook-rmc-anticorrosive =
    Лечит [color=green]{$heal}[/color] ед. ожогов.
    Передозировка наносит [color=red]{$odBrute}[/color] ед. травмы и [color=red]{$odToxin}[/color] ед. токсинов.
    Критическая передозировка наносит [color=red]{$critBrute}[/color] ед. травмы и [color=red]{$critToxin}[/color] ед. токсинов

reagent-effect-guidebook-rmc-hemogenic-prefix = Наносит [color=red]{$brute}[/color] ед. травмы, [color=red]{$airloss}[/color] ед. урона от удушья и замедляет вас.

reagent-effect-guidebook-rmc-hemogenic =
    Восстанавливает [color=green]{$restore}[/color] мл крови, если вы не голодны.
    Вызывает потерю [color=red]{$loss}[/color] ед. питания в секунду.
    Передозировка наносит [color=red]{$odToxin}[/color] ед. токсинов.
    Критическая передозировка вызывает дополнительную потерю [color=red]{$critLoss}[/color] ед. питания

reagent-effect-guidebook-rmc-neogenetic =
    Лечит [color=green]{$heal}[/color] ед. травмы.
    Передозировка наносит [color=red]{$overdose}[/color] ед. ожогов.
    Критическая передозировка наносит [color=red]{$critBurn}[/color] ед. ожогов и [color=red]{$critToxin}[/color] ед. токсинов

reagent-effect-guidebook-rmc-oxygenating =
    {$powerful ->
        [true] Лечит [color=green]весь[/color] урон от удушья и удаляет [color=green]{ $amount }[/color] ед. Лексорина из кровотока.
       *[false] Лечит [color=green]{$amount}[/color] ед. урона от удушья и удаляет [color=green]{ $amount }[/color] ед. Лексорина из кровотока.
    }
    Передозировка наносит [color=red]{$odToxin}[/color] ед. токсинов.
    Критическая передозировка наносит [color=red]{$critBrute}[/color] ед. травмы и [color=red]{$critToxin}[/color] ед. токсинов

reagent-effect-guidebook-rmc-remove-damage = Убирает весь урон типа {$group}

reagent-effect-guidebook-rmc-boosting = Усиливает потенцию всех остальных свойств этого химиката на [color=yellow]{$amount}[/color]

reagent-effect-guidebook-rmc-stabilize-temperature = Стабилизирует температуру тела, в котором находится, до {$stable} градусов, изменяя её на {$change} градусов за раз
