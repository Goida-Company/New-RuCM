reagent-effect-guidebook-rmc-antitoxic =
    Heals [color=green]{ $healing }[/color] toxin damage and removes [color=green]0.125[/color] units of toxic chemicals from the bloodstream.
    Critical overdoses cause [color=red]5[/color] seconds of unconsciousness with a [color=red]5%[/color] chance

reagent-effect-guidebook-rmc-biocidic =
    Deals [color=red]{ $damage }[/color] brute damage.
    Overdoses cause [color=red]{ $overdose }[/color] brute damage.
    Critical overdoses cause [color=red]{ $critical }[/color] brute damage

reagent-effect-guidebook-rmc-carcinogenic =
    Deals [color=red]{ $genetic }[/color] genetic damage.
    Overdoses cause [color=red]{ $overdose }[/color] genetic damage.
    Critical overdoses cause [color=red]{ $critical }[/color] brute damage

reagent-effect-guidebook-rmc-alchemist-pain = Increases pain by [color=red]{ $amount }[/color] per second

reagent-effect-guidebook-rmc-alchemist-purge = Purges [color=red]{ $amount }[/color] units of matching non-toxin chemicals per second

reagent-effect-guidebook-rmc-ketogenic =
    Removes [color=red]{ $nutrients }[/color] nutrients, causing hunger over time.
    Increases alcohol metabolism rate by [color=green]{ $alcohol }[/color] units.
    Overdoses cause [color=red]{ $odNutrition }[/color] nutrition loss, [color=red]{ $odToxin }[/color] toxin damage, and a [color=red]{ $odChance }%[/color] chance of vomiting.
    Critical overdoses will knock you unconscious for [color=red]10[/color] seconds

reagent-effect-guidebook-rmc-thermostabilizing =
    Stabilizes the temperature of the body to [color=green]{ $target }[/color] kelvins, by [color=green]{ $step }[/color] K at a time.
    Overdoses cause [color=red]10[/color] seconds of unconsciousness.
    Critical overdoses cause [color=red]5[/color] seconds of unconsciousness with a [color=red]5%[/color] chance

reagent-effect-guidebook-rmc-electrogenetic =
    Heals [color=green]{ $heal }[/color] brute, burn, and toxin damage when defibrillated.
    Removes 1u of this chemical from the solution when defibrillated

reagent-effect-guidebook-rmc-corrosive =
    Deals [color=red]{ $damage }[/color] burn damage.
    Overdoses cause [color=red]{ $overdose }[/color] burn damage.
    Critical overdoses cause [color=red]{ $critical }[/color] burn damage

reagent-effect-guidebook-rmc-hypoxemic =
    Deals [color=red]{ $airloss }[/color] airloss damage and causes the victim to gasp for air.
    Overdoses cause [color=red]{ $odBrute }[/color] brute, [color=red]{ $odToxin }[/color] toxin, and [color=red]{ $odAirloss }[/color] airloss damage.
    Critical overdoses cause [color=red]{ $critBrute }[/color] brute and [color=red]{ $critToxin }[/color] toxin damage

reagent-effect-guidebook-rmc-toxic =
    Deals [color=red]{ $damage }[/color] toxin damage.
    Overdoses cause [color=red]{ $overdose }[/color] toxin damage.
    Critical overdoses cause [color=red]{ $critical }[/color] toxin damage

reagent-effect-guidebook-rmc-antihallucinogenic =
    Removes [color=green]2.5[/color] units of Mindbreaker Toxin and Space Drugs from the bloodstream. It also stabilizes perceptive abnormalities such as hallucinations.
    Overdoses cause [color=red]{ $odToxin }[/color] toxin damage.
    Critical overdoses cause [color=red]{ $critBrute }[/color] brute, [color=red]{ $critBurn }[/color] burn, and [color=red]{ $critToxin }[/color] toxin damage

reagent-effect-guidebook-rmc-focusing =
    Removes [color=green]{ $alcohol }[/color] units of alcoholic substances and [color=green]{ $drunk }[/color] seconds of drunkenness{ $powerful ->
        [true] . Also powerful enough to instantly cure mute and blindness.
       *[false] .
    }
    Overdoses cause [color=red]{ $odToxin }[/color] toxin damage.
    Critical overdoses cause [color=red]{ $critToxin }[/color] toxin damage

reagent-effect-guidebook-rmc-nutritious = Restores [color=green]{ $amount }[/color] nutrients to the body and satiates hunger

reagent-effect-guidebook-rmc-anticarcinogenic =
    Heals [color=green]{ $heal }[/color] genetic damage.
    Overdoses cause [color=red]{ $overdose }[/color] toxin damage.
    Critical overdoses cause [color=red]{ $critical }[/color] brute damage

reagent-effect-guidebook-rmc-anticorrosive =
    Heals [color=green]{ $heal }[/color] burn damage.
    Overdoses cause [color=red]{ $odBrute }[/color] brute and [color=red]{ $odToxin }[/color] toxin damage.
    Critical overdoses cause [color=red]{ $critBrute }[/color] brute and [color=red]{ $critToxin }[/color] toxin damage

reagent-effect-guidebook-rmc-hemogenic-prefix = Deals [color=red]{ $brute }[/color] brute, [color=red]{ $airloss }[/color] airloss damage, and slows you down.

reagent-effect-guidebook-rmc-hemogenic =
    Restores [color=green]{ $restore }[/color]cl of blood while not hungry.
    Causes [color=red]{ $loss }[/color] nutrient loss per second.
    Overdoses cause [color=red]{ $odToxin }[/color] toxin damage.
    Critical overdoses cause [color=red]{ $critLoss }[/color] additional nutrient loss

reagent-effect-guidebook-rmc-neogenetic =
    Heals [color=green]{ $heal }[/color] brute damage.
    Overdoses cause [color=red]{ $overdose }[/color] burn damage.
    Critical overdoses cause [color=red]{ $critBurn }[/color] burn and [color=red]{ $critToxin }[/color] toxin damage

reagent-effect-guidebook-rmc-oxygenating =
    { $powerful ->
        [true] Heals [color=green]all[/color] airloss damage and removes [color=green]{ $amount }[/color] Lexorin from the bloodstream.
       *[false] Heals [color=green]{ $amount }[/color] airloss damage and removes [color=green]{ $amount }[/color] Lexorin from the bloodstream.
    }
    Overdoses cause [color=red]{ $odToxin }[/color] toxin damage.
    Critical overdoses cause [color=red]{ $critBrute }[/color] brute and [color=red]{ $critToxin }[/color] toxin damage

reagent-effect-guidebook-rmc-remove-damage = Removes all { $group } damage

reagent-effect-guidebook-rmc-boosting = Boosts the potency of all other properties in this chemical by [color=yellow]{ $amount }[/color]

reagent-effect-guidebook-rmc-stabilize-temperature = Stabilizes the temperature of the body that it is in to { $stable } degrees, by { $change } degrees at a time
