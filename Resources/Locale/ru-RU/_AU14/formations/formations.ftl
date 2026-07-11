# Formation control window.
formation-window-title = Управление формацией
formation-status-none = Активной формации нет.
formation-status-planning = Размещение активно - кликайте по тайлам, чтобы ставить маркеры отделения.
formation-status-active = Формация активна - участников в формации: { $count }.
formation-status-open-slots = Открытых мест: { $count } - ожидание бойцов.
formation-status-staged = Маркеры подготовлены. Нажмите "Активировать", когда будете готовы.
formation-status-start = Активной формации нет. Начните с шага 1.

formation-step-mark = Шаг 1 - отметьте позиции
formation-place-marker = Поставить маркер отделения
formation-place-marker-tooltip = Поставьте маркер позиции для любого бойца. Кликните по тайлу, чтобы разместить его; стрелка смотрит туда же, куда смотрит ваш боец.
formation-undo-last = Отменить последний
formation-staged-markers = Подготовленные маркеры:
formation-pending-dot =   { $type } ({ $x }, { $y }) смотрит { $facing }
formation-dot-type-leader = [Лидер]
formation-dot-type-squad = [Отделение]
formation-none-staged =   (нет подготовленных)

formation-dot-lifetime-standard = Жизнь маркеров: стандартно (2 мин)
formation-dot-lifetime-extended = Жизнь маркеров: продленно (15 мин)  [АКТИВНО]
formation-dot-lifetime-tooltip = Стандартно: маркеры формации исчезают через 2 минуты - это подходит почти для всех ситуаций. Продленно: маркеры живут 15 минут. Включайте только для долгих статичных оборонительных позиций, где места действительно должны оставаться открытыми долго.
formation-extended-warning-title = ! ПРОДЛЕННОЕ ВРЕМЯ ЖИЗНИ АКТИВНО !
formation-extended-warning-duration = Маркеры останутся на карте на 15 минут.
formation-extended-warning-rare = Это предназначено только для редких долгих статичных операций.
formation-extended-warning-abuse = Злоупотребление этим режимом ПОВЛЕЧЕТ последствия.
formation-extended-warning-reset = Вернитесь к стандартному режиму как можно скорее.

formation-step-activate = Шаг 2 - активируйте формацию
formation-activate = Активировать
formation-activate-tooltip = Разместить все подготовленные маркеры на карте. У бойцов есть 2 минуты, чтобы подойти и занять места (или 15 минут, если включено продленное время жизни).
formation-clear-staged = Очистить подготовленные
formation-clear-staged-tooltip = Удалить все подготовленные маркеры, не размещая их.

formation-step-manage = Шаг 3 - управляйте формацией
formation-march = Начать движение формации
formation-halt = Остановить формацию
formation-halt-tooltip = Остановка прекращает передачу вашего движения формации. Движение возобновляет ее. Формация изначально остановлена, чтобы у вас было время на инструктаж.
formation-follow-mode = Режим следования
formation-hold = Удержание
formation-hold-active = [Активно] Удержание
formation-hold-tooltip = Удержание - ведомые двигаются ровно на один тайл каждый раз, когда двигаетесь вы. Чисто и синхронно на открытой местности.
    
    Совет: сначала переключитесь на преследование, если люди отстали, затем вернитесь к удержанию, когда формация снова станет плотной.
formation-chase = Преследование
formation-chase-active = [Активно] Преследование
formation-chase-tooltip = Преследование - ведомые каждый тик сближаются со своим местом, даже если вы не двигались. Разрывы быстро закрываются после поворотов или рывков.
    
    Совет: лучше всего подходит для перестроения после быстрого продвижения или когда участники рассыпались. Вернитесь к удержанию, когда все снова займут места.

formation-collision = Коллизии
formation-collisions-off = Коллизии: выкл.
formation-collisions-on = Коллизии: вкл.
formation-collisions-tooltip = Выкл. - участники свободно проходят друг через друга, что сохраняет плавность движения в узких коридорах. Вкл. возвращает обычную физику, и участники снова блокируют друг друга.
formation-remove-open-slots = Убрать открытые места
formation-remove-open-slots-tooltip = Удалить все маркеры, которые еще не занял боец.
formation-disband = Распустить
formation-disband-tooltip = Вывести всех из формации и удалить все маркеры.
formation-counts-zero = Открытых мест: 0  |  Участников: 0
formation-counts = Открытых мест: { $slots }  |  Участников: { $members }
formation-debug = Отладка
formation-show-slots = Показать позиции мест
formation-hide-slots = Скрыть позиции мест
formation-show-slots-tooltip = Показывать постоянные белые маркеры на вычисленной целевой позиции каждого участника. Удобно для проверки формы построения после поворота.
