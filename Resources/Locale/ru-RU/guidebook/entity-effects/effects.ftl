entity-effect-guidebook-spawn-entity =
    { $chance ->
        [1] Создаёт
        *[other] создают
    } { $amount ->
        [1] { INDEFINITE($entname) }
        *[other] { $amount } { MAKEPLURAL($entname) }
    }

entity-effect-guidebook-destroy =
    { $chance ->
        [1] Уничтожает
        *[other] уничтожают
    } объект

entity-effect-guidebook-break =
    { $chance ->
        [1] Ломает
        *[other] ломают
    } объект

entity-effect-guidebook-explosion =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } взрыв

entity-effect-guidebook-emp =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } электромагнитный импульс

entity-effect-guidebook-flash =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } ослепляющую вспышку

entity-effect-guidebook-foam-area =
    { $chance ->
        [1] Создаёт
        *[other] создают
    } большое количество пены

entity-effect-guidebook-smoke-area =
    { $chance ->
        [1] Создаёт
        *[other] создают
    } большое количество дыма

entity-effect-guidebook-satiate-thirst =
    { $chance ->
        [1] Утоляет
        *[other] утоляют
    } { $relative ->
        [1] жажду в среднем темпе
        *[other] жажду в { NATURALFIXED($relative, 3) }x от среднего темпа
    }

entity-effect-guidebook-satiate-hunger =
    { $chance ->
        [1] Утоляет
        *[other] утоляют
    } { $relative ->
        [1] голод в среднем темпе
        *[other] голод в { NATURALFIXED($relative, 3) }x от среднего темпа
    }

entity-effect-guidebook-health-change =
    { $chance ->
        [1] { $healsordeals ->
                [heals] Лечит
                [deals] Наносит
                *[both] Изменяет здоровье на
            }
        *[other] { $healsordeals ->
                [heals] лечат
                [deals] наносят
                *[both] изменяют здоровье на
            }
    } { $changes }

entity-effect-guidebook-even-health-change =
    { $chance ->
        [1] { $healsordeals ->
                [heals] Равномерно лечит
                [deals] Равномерно наносит
                *[both] Равномерно изменяет здоровье на
            }
        *[other] { $healsordeals ->
                [heals] равномерно лечат
                [deals] равномерно наносят
                *[both] равномерно изменяют здоровье на
            }
    } { $changes }

entity-effect-guidebook-status-effect-old =
    { $type ->
        [update]{ $chance ->
                [1] Вызывает
                *[other] вызывают
            } нокдаун минимум на { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            }, эффект не накапливается
        [add]   { $chance ->
                [1] Вызывает
                *[other] вызывают
            } { LOC($key) } минимум на { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            }, эффект накапливается
        [set]  { $chance ->
                [1] Вызывает
                *[other] вызывают
            } { LOC($key) } на { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            }, эффект не накапливается
        *[remove]{ $chance ->
                [1] Удаляет
                *[other] удаляют
            } { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            } от { LOC($key) }
    }

entity-effect-guidebook-status-effect =
    { $type ->
        [update]{ $chance ->
                [1] Вызывает
                *[other] вызывают
            } { LOC($key) } минимум на { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            }, эффект не накапливается
        [add] { $chance ->
                [1] Вызывает
                *[other] вызывают
            } { LOC($key) } минимум на { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            }, эффект накапливается
        [set] { $chance ->
                [1] Вызывает
                *[other] вызывают
            } { LOC($key) } минимум на { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            }, эффект не накапливается
        *[remove] { $chance ->
                [1] Удаляет
                *[other] удаляют
            } { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            } от { LOC($key) }
    } { $delay ->
        [0] немедленно
        *[other] после { NATURALFIXED($delay, 3) } { $delay ->
                [one] секунду
                [few] секунды
                *[other] секунд
            } задержки
    }

entity-effect-guidebook-status-effect-indef =
    { $type ->
        [update]{ $chance ->
                [1] Вызывает
                *[other] вызывает
            } постоянный { LOC($key) }
        [add]   { $chance ->
                [1] Вызывает
                *[other] вызывают
            } постоянный{ LOC($key) }
        [set]  { $chance ->
                [1] Вызывает
                *[other] вызывают
            } постоянный{ LOC($key) }
        *[remove]{ $chance ->
                [1] Убирает
                *[other] убирают
            } { LOC($key) }
    } { $delay ->
        [0] мгновенно
        *[other] после { NATURALFIXED($delay, 3) } { $delay ->
                [one] секунду
                [few] секунды
                *[other] секунд
            } задержки
    }

entity-effect-guidebook-knockdown =
    { $type ->
        [update]{ $chance ->
                [1] Вызывает
                *[other] вызывают
            } нокдаун минимум на { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            }, эффект не накапливается
        [add]   { $chance ->
                [1] Вызывает
                *[other] вызывают
            } нокдаун минимум на { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            }, эффект накапливается
        *[set]  { $chance ->
                [1] Вызывает
                *[other] вызывают
            } нокдаун минимум на { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            }, эффект не накапливается
        [remove]{ $chance ->
                [1] Удаляет
                *[other] удаляют
            } { NATURALFIXED($time, 3) } { $time ->
                [one] секунду
                [few] секунды
                *[other] секунд
            } нокдауна
    }

entity-effect-guidebook-set-solution-temperature-effect =
    { $chance ->
        [1] Устанавливает
        *[other] устанавливают
    } температуру раствора ровно в { NATURALFIXED($temperature, 2) }К

entity-effect-guidebook-adjust-solution-temperature-effect =
    { $chance ->
        [1] { $deltasign ->
                [1] Добавляет
                *[-1] Удаляет
            }
        *[other] { $deltasign ->
                [1] добавляют
                *[-1] удаляют
            }
    } тепло { $deltasign ->
        [1] в раствор, пока он не достигнет не более { NATURALFIXED($maxtemp, 2) }К
        *[-1] из раствора, пока он не достигнет не менее { NATURALFIXED($mintemp, 2) }К
    }

entity-effect-guidebook-adjust-reagent-reagent =
    { $chance ->
        [1] { $deltasign ->
                [1] Добавляет
                *[-1] Удаляет
            }
        *[other] { $deltasign ->
                [1] добавляют
                *[-1] удаляют
            }
    } { NATURALFIXED($amount, 2) }ед. { $reagent } { $deltasign ->
        [1] в
        *[-1] из
    } раствора

entity-effect-guidebook-adjust-reagent-group =
    { $chance ->
        [1] { $deltasign ->
                [1] Добавляет
                *[-1] Удаляет
            }
        *[other] { $deltasign ->
                [1] добавляют
                *[-1] удаляют
            }
    } { NATURALFIXED($amount, 2) }ед. реагентов из группы { $group } { $deltasign ->
        [1] в
        *[-1] из
    } раствора

entity-effect-guidebook-adjust-temperature =
    { $chance ->
        [1] { $deltasign ->
                [1] Добавляет
                *[-1] Удаляет
            }
        *[other] { $deltasign ->
                [1] добавляют
                *[-1] удаляют
            }
    } { POWERJOULES($amount) } тепла { $deltasign ->
        [1] в
        *[-1] из
    } тело носителя

entity-effect-guidebook-chem-cause-disease =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } болезнь { $disease }

entity-effect-guidebook-chem-cause-random-disease =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } болезни { $diseases }

entity-effect-guidebook-jittering =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } дрожь

entity-effect-guidebook-clean-bloodstream =
    { $chance ->
        [1] Очищает
        *[other] очищают
    } кровоток от других химических веществ

entity-effect-guidebook-cure-disease =
    { $chance ->
        [1] Лечит
        *[other] лечат
    } болезни

entity-effect-guidebook-eye-damage =
    { $chance ->
        [1] { $deltasign ->
                [1] Наносит
                *[-1] Лечит
            }
        *[other] { $deltasign ->
                [1] наносят
                *[-1] лечат
            }
    } повреждения глаз

entity-effect-guidebook-vomit =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } рвоту

entity-effect-guidebook-create-gas =
    { $chance ->
        [1] Создаёт
        *[other] создают
    } { $moles } моль { $gas }

entity-effect-guidebook-drunk =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } опьянение

entity-effect-guidebook-electrocute =
    { $chance ->
        [1] Бьёт током
        *[other] бьют током
    } носителя на { NATURALFIXED($time, 3) } { $time ->
        [one] секунду
        [few] секунды
        *[other] секунд
    }

entity-effect-guidebook-emote =
    { $chance ->
        [1] Заставляет
        *[other] заставляют
    } носителя [bold][color=white]{ $emote }[/color][/bold]

entity-effect-guidebook-extinguish-reaction =
    { $chance ->
        [1] Тушит
        *[other] тушат
    } огонь

entity-effect-guidebook-flammable-reaction =
    { $chance ->
        [1] Увеличивает
        *[other] увеличивают
    } воспламеняемость

entity-effect-guidebook-ignite =
    { $chance ->
        [1] Поджигает
        *[other] поджигают
    } носителя

entity-effect-guidebook-make-sentient =
    { $chance ->
        [1] Делает
        *[other] делают
    } носителя разумным

entity-effect-guidebook-make-polymorph =
    { $chance ->
        [1] Превращает
        *[other] превращают
    } носителя в { $entityname }

entity-effect-guidebook-modify-bleed-amount =
    { $chance ->
        [1] { $deltasign ->
                [1] Вызывает
                *[-1] Уменьшает
            }
        *[other] { $deltasign ->
                [1] вызывают
                *[-1] уменьшают
            }
    } кровотечение

entity-effect-guidebook-modify-blood-level =
    { $chance ->
        [1] { $deltasign ->
                [1] Увеличивает
                *[-1] Уменьшает
            }
        *[other] { $deltasign ->
                [1] увеличивают
                *[-1] уменьшают
            }
    } уровень крови

entity-effect-guidebook-paralyze =
    { $chance ->
        [1] Парализует
        *[other] парализуют
    } носителя минимум на { NATURALFIXED($time, 3) } { $time ->
        [one] секунду
        [few] секунды
        *[other] секунд
    }

entity-effect-guidebook-movespeed-modifier =
    { $chance ->
        [1] Изменяет
        *[other] изменяют
    } скорость передвижения в { NATURALFIXED($sprintspeed, 3) }x минимум на { NATURALFIXED($time, 3) } { $time ->
        [one] секунду
        [few] секунды
        *[other] секунд
    }

entity-effect-guidebook-reset-narcolepsy =
    { $chance ->
        [1] Временно подавляет
        *[other] временно подавляют
    } нарколепсию

entity-effect-guidebook-wash-cream-pie-reaction =
    { $chance ->
        [1] Смывает
        *[other] смывают
    } крем-пирог с лица

entity-effect-guidebook-cure-zombie-infection =
    { $chance ->
        [1] Лечит
        *[other] лечат
    } активную зомби-инфекцию

entity-effect-guidebook-cause-zombie-infection =
    { $chance ->
        [1] Заражает
        *[other] заражают
    } существо зомби-вирусом

entity-effect-guidebook-innoculate-zombie-infection =
    { $chance ->
        [1] Лечит
        *[other] лечат
    } зомби-вирус и обеспечивает иммунитет к нему в будущем

entity-effect-guidebook-reduce-rotting =
    { $chance ->
        [1] Восстанавливает
        *[other] восстанавливают
    } { NATURALFIXED($time, 3) } { $time ->
        [one] секунду
        [few] секунды
        *[other] секунд
    } разложения

entity-effect-guidebook-area-reaction =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } дымовую или пенную реакцию на { NATURALFIXED($duration, 3) } { $duration ->
        [one] секунду
        [few] секунды
        *[other] секунд
    }

entity-effect-guidebook-add-to-solution-reaction =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } добавление { $reagent } во внутренний контейнер раствора

entity-effect-guidebook-artifact-unlock =
    { $chance ->
        [1] Помогает
        *[other] помогают
    } разблокировать инопланетный артефакт.

entity-effect-guidebook-artifact-durability-restore =
    Восстанавливает { $restored } прочности в активных узлах инопланетного артефакта.

entity-effect-guidebook-plant-attribute =
    { $chance ->
        [1] Изменяет
        *[other] изменяют
    } { $attribute } на { $positive ->
        [true] [color=red]{ $amount }[/color]
        *[false] [color=green]{ $amount }[/color]
    }

entity-effect-guidebook-plant-cryoxadone =
    { $chance ->
        [1] Омолаживает
        *[other] омолаживают
    } растение, в зависимости от возраста растения и времени его роста

entity-effect-guidebook-plant-phalanximine =
    { $chance ->
        [1] Восстанавливает
        *[other] восстанавливают
    } жизнеспособность растения, ставшего нежизнеспособным в результате мутации

entity-effect-guidebook-plant-diethylamine =
    { $chance ->
        [1] Повышает
        *[other] повышают
    } продолжительность жизни растения и/или его базовое здоровье с шансом 10% на единицу

entity-effect-guidebook-plant-robust-harvest =
    { $chance ->
        [1] Повышает
        *[other] повышают
    } потенцию растения путём { $increase } до максимума в { $limit }. Приводит к тому, что растение теряет свои семена, когда потенция достигает { $seedlesstreshold }. Попытка повысить потенцию свыше { $limit } может вызвать снижение урожайности с вероятностью 10%

entity-effect-guidebook-plant-seeds-add =
    { $chance ->
        [1] Восстанавливает
        *[other] восстанавливают
    } семена растения

entity-effect-guidebook-plant-seeds-remove =
    { $chance ->
        [1] Убирает
        *[other] убирают
    } семена из растения

entity-effect-guidebook-plant-mutate-chemicals =
    { $chance ->
        [1] Мутирует
        *[other] мутируют
    } растение, чтобы то производило { $name }
