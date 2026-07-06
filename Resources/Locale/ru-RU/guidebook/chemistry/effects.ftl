-create-3rd-person =
    { $chance ->
        [1] Создаёт
        *[other] создают
    }

-cause-3rd-person =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    }

-satiate-3rd-person =
    { $chance ->
        [1] Утоляет
        *[other] утоляют
    }

reagent-effect-guidebook-create-entity-reaction-effect =
    { $chance ->
        [1] Создаёт
        *[other] создают
    } { $amount ->
        [1] {INDEFINITE($entname)}
        *[other] {$amount} {MAKEPLURAL($entname)}
    }

reagent-effect-guidebook-explosion-reaction-effect =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } взрыв

reagent-effect-guidebook-emp-reaction-effect =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } электромагнитный импульс

reagent-effect-guidebook-flash-reaction-effect =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } ослепляющую вспышку

reagent-effect-guidebook-foam-area-reaction-effect =
    { $chance ->
        [1] Создаёт
        *[other] создают
    } большое количество пены

reagent-effect-guidebook-smoke-area-reaction-effect =
    { $chance ->
        [1] Создаёт
        *[other] создают
    } большое количество дыма

reagent-effect-guidebook-satiate-thirst =
    { $chance ->
        [1] Утоляет
        *[other] утоляют
    } { $relative ->
        [1] жажду в среднем темпе
        *[other] жажду в {NATURALFIXED($relative, 3)}x от среднего темпа
    }

reagent-effect-guidebook-satiate-hunger =
    { $chance ->
        [1] Утоляет
        *[other] утоляют
    } { $relative ->
        [1] голод в среднем темпе
        *[other] голод в {NATURALFIXED($relative, 3)}x от среднего темпа
    }

reagent-effect-guidebook-health-change =
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

reagent-effect-guidebook-even-health-change =
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

reagent-effect-guidebook-status-effect =
    { $type ->
        [add]   { $chance ->
                    [1] Вызывает
                    *[other] вызывают
                } {LOC($key)} минимум на {NATURALFIXED($time, 3)} { $time ->
                    [one] секунду
                    [few] секунды
                    *[other] секунд
                }, эффект накапливается
        *[set]  { $chance ->
                    [1] Вызывает
                    *[other] вызывают
                } {LOC($key)} минимум на {NATURALFIXED($time, 3)} { $time ->
                    [one] секунду
                    [few] секунды
                    *[other] секунд
                }, эффект не накапливается
        [remove]{ $chance ->
                    [1] Удаляет
                    *[other] удаляют
                } {NATURALFIXED($time, 3)} { $time ->
                    [one] секунду
                    [few] секунды
                    *[other] секунд
                } от {LOC($key)}
    }

reagent-effect-guidebook-set-solution-temperature-effect =
    { $chance ->
        [1] Устанавливает
        *[other] устанавливают
    } температуру раствора ровно в {NATURALFIXED($temperature, 2)}К

reagent-effect-guidebook-adjust-solution-temperature-effect =
    { $chance ->
        [1] { $deltasign ->
                [1] Добавляет
                *[-1] Удаляет
            }
        *[other]
            { $deltasign ->
                [1] добавляют
                *[-1] удаляют
            }
    } тепло { $deltasign ->
                [1] в раствор, пока он не достигнет не более {NATURALFIXED($maxtemp, 2)}К
                *[-1] из раствора, пока он не достигнет не менее {NATURALFIXED($mintemp, 2)}К
            }

reagent-effect-guidebook-adjust-reagent-reagent =
    { $chance ->
        [1] { $deltasign ->
                [1] Добавляет
                *[-1] Удаляет
            }
        *[other]
            { $deltasign ->
                [1] добавляют
                *[-1] удаляют
            }
    } {NATURALFIXED($amount, 2)}ед. {$reagent} { $deltasign ->
        [1] в
        *[-1] из
    } раствора

reagent-effect-guidebook-adjust-reagent-group =
    { $chance ->
        [1] { $deltasign ->
                [1] Добавляет
                *[-1] Удаляет
            }
        *[other]
            { $deltasign ->
                [1] добавляют
                *[-1] удаляют
            }
    } {NATURALFIXED($amount, 2)}ед. реагентов из группы {$group} { $deltasign ->
            [1] в
            *[-1] из
        } раствора

reagent-effect-guidebook-adjust-temperature =
    { $chance ->
        [1] { $deltasign ->
                [1] Добавляет
                *[-1] Удаляет
            }
        *[other]
            { $deltasign ->
                [1] добавляют
                *[-1] удаляют
            }
    } {POWERJOULES($amount)} тепла { $deltasign ->
            [1] в
            *[-1] из
        } тело носителя

reagent-effect-guidebook-chem-cause-disease =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } болезнь { $disease }

reagent-effect-guidebook-chem-cause-random-disease =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } болезни { $diseases }

reagent-effect-guidebook-jittering =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } дрожь

reagent-effect-guidebook-chem-clean-bloodstream =
    { $chance ->
        [1] Очищает
        *[other] очищают
    } кровоток от других химических веществ

reagent-effect-guidebook-cure-disease =
    { $chance ->
        [1] Лечит
        *[other] лечат
    } болезни

reagent-effect-guidebook-cure-eye-damage =
    { $chance ->
        [1] { $deltasign ->
                [1] Наносит
                *[-1] Лечит
            }
        *[other]
            { $deltasign ->
                [1] наносят
                *[-1] лечат
            }
    } повреждения глаз

reagent-effect-guidebook-chem-vomit =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } рвоту

reagent-effect-guidebook-create-gas =
    { $chance ->
        [1] Создаёт
        *[other] создают
    } { $moles } моль { $gas }

reagent-effect-guidebook-drunk =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } опьянение

reagent-effect-guidebook-electrocute =
    { $chance ->
        [1] Бьёт током
        *[other] бьют током
    } носителя на {NATURALFIXED($time, 3)} { $time ->
        [one] секунду
        [few] секунды
        *[other] секунд
    }

reagent-effect-guidebook-emote =
    { $chance ->
        [1] Заставляет
        *[other] заставляют
    } носителя [bold][color=white]{$emote}[/color][/bold]

reagent-effect-guidebook-extinguish-reaction =
    { $chance ->
        [1] Тушит
        *[other] тушат
    } огонь

reagent-effect-guidebook-flammable-reaction =
    { $chance ->
        [1] Увеличивает
        *[other] увеличивают
    } воспламеняемость

reagent-effect-guidebook-ignite =
    { $chance ->
        [1] Поджигает
        *[other] поджигают
    } носителя

reagent-effect-guidebook-make-sentient =
    { $chance ->
        [1] Делает
        *[other] делают
    } носителя разумным

reagent-effect-guidebook-make-polymorph =
    { $chance ->
        [1] Превращает
        *[other] превращают
    } носителя в { $entityname }

reagent-effect-guidebook-modify-bleed-amount =
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

reagent-effect-guidebook-modify-blood-level =
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

reagent-effect-guidebook-paralyze =
    { $chance ->
        [1] Парализует
        *[other] парализуют
    } носителя минимум на {NATURALFIXED($time, 3)} { $time ->
        [one] секунду
        [few] секунды
        *[other] секунд
    }

reagent-effect-guidebook-movespeed-modifier =
    { $chance ->
        [1] Изменяет
        *[other] изменяют
    } скорость передвижения в {NATURALFIXED($walkspeed, 3)}x минимум на {NATURALFIXED($time, 3)} { $time ->
        [one] секунду
        [few] секунды
        *[other] секунд
    }

reagent-effect-guidebook-reset-narcolepsy =
    { $chance ->
        [1] Временно подавляет
        *[other] временно подавляют
    } нарколепсию

reagent-effect-guidebook-wash-cream-pie-reaction =
    { $chance ->
        [1] Смывает
        *[other] смывают
    } крем-пирог с лица

reagent-effect-guidebook-cure-zombie-infection =
    { $chance ->
        [1] Лечит
        *[other] лечат
    } активную зомби-инфекцию

reagent-effect-guidebook-cause-zombie-infection =
    { $chance ->
        [1] Заражает
        *[other] заражают
    } существо зомби-вирусом

reagent-effect-guidebook-innoculate-zombie-infection =
    { $chance ->
        [1] Лечит
        *[other] лечат
    } активную зомби-инфекцию и обеспечивает иммунитет к будущим заражениям

reagent-effect-guidebook-reduce-rotting =
    { $chance ->
        [1] Восстанавливает
        *[other] восстанавливают
    } {NATURALFIXED($time, 3)} { $time ->
        [one] секунду
        [few] секунды
        *[other] секунд
    } разложения

reagent-effect-guidebook-area-reaction =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } дымовую или пенную реакцию на {NATURALFIXED($duration, 3)} { $duration ->
        [one] секунду
        [few] секунды
        *[other] секунд
    }

reagent-effect-guidebook-add-to-solution-reaction =
    { $chance ->
        [1] Вызывает
        *[other] вызывают
    } добавление нанесённых на объект химических веществ во внутренний контейнер раствора

reagent-effect-guidebook-artifact-unlock =
    { $chance ->
        [1] Помогает
        *[other] помогают
        } разблокировать инопланетный артефакт.

reagent-effect-guidebook-artifact-durability-restore =
    Восстанавливает {$restored} прочности в активных узлах инопланетного артефакта.

reagent-effect-guidebook-plant-attribute =
    { $chance ->
        [1] Изменяет
        *[other] изменяют
    } {$attribute} на [color={$colorName}]{$amount}[/color]

reagent-effect-guidebook-plant-cryoxadone =
    { $chance ->
        [1] Омолаживает
        *[other] омолаживают
    } растение в зависимости от его возраста и времени роста

reagent-effect-guidebook-plant-phalanximine =
    { $chance ->
        [1] Восстанавливает
        *[other] восстанавливают
    } жизнеспособность растения, ставшего нежизнеспособным в результате мутации

reagent-effect-guidebook-plant-diethylamine =
    { $chance ->
        [1] Увеличивает
        *[other] увеличивают
    } продолжительность жизни растения и/или его базовое здоровье с шансом 10% на каждое

reagent-effect-guidebook-plant-robust-harvest =
    { $chance ->
        [1] Повышает
        *[other] повышают
    } потенцию растения на {$increase} до максимума в {$limit}. Приводит к тому, что растение теряет свои семена, когда потенция достигает {$seedlesstreshold}. Попытка повысить потенцию свыше {$limit} может вызвать снижение урожайности с вероятностью 10%

reagent-effect-guidebook-plant-seeds-add =
    { $chance ->
        [1] Восстанавливает
        *[other] восстанавливают
    } семена растения

reagent-effect-guidebook-plant-seeds-remove =
    { $chance ->
        [1] Убирает
        *[other] убирают
    } семена из растения
