# U2-05 evidence: the harvest planner and the harvest report

Roadmap item U2-05. Branch `wave3/ui-a-2`. Frames are 1280 px downscales in
`u2_05/`, captured by `tools/capture_screenshots.sh --scene
res://qa/scenes/HarvestScreensPreview.tscn` (po3_red_sone, seed 91, grown
until a site's fish reach 4.5 kg: day 294, week 42, October of year 1).

## Frames

| frame | what it shows |
|---|---|
| `41a_harvest_planner.jpg` | "Høst" on PO3 Nord: six pens picked, the weight histogram in gutted tonnes per size class with each class's price factor (2-3 kg in the discount tone, 4-5 kg the outlined reference), grades, price, wellboat and slaughterhouse, the settlement, and now / 2 / 4 / 8 weeks side by side. The default verb is the planned order ("Bestill slakting · brønnbåt 27. okt. 2027"), express the secondary ("Ekspress i dag · 17,10 MNOK"). The sheet is as tall as its content. |
| `41b_harvest_planner_wait.jpg` | The "Om 8 uker" column shown in detail: the fish at 5.72 kg, 305 t gutted, the price expected in December, 2.04 MNOK of feed, 20.43 MNOK net after feed (tagged best). |
| `41c_harvest_report_express.jpg` | PO3 Sør sold express: the report opens at once with the net, the fish, the histogram, the grades and the settlement including the express surcharge; the harvest's one toast at the bottom right. |
| `41d_harvest_wellboat.jpg` | The planned order placed: the large harvest carrier lies alongside PO3 Nord, chip "Slakting · om 5 d" (the feed barge on the left). |
| `41f_harvest_wellboat_payoff.jpg` | The order's two slaughter days done: the carrier, about to sail home, carries the harvest's net on its chip, "Slakting · ferdig · +17,29 MNOK". |
| `41e_harvest_report_planned.jpg` | The planned harvest's report: 2 slaughter days summed, net +17 291 552 NOK, "Bokført i regnskapet" with the ledger's harvest sales and cost. |

## Acceptance (roadmap U2-05)

| # | criterion | evidence |
|---|---|---|
| 1 | Report figures equal the ledger entries | UI test (`_test_harvest_planner_orders_and_reports`): the report's gross, cost and net are the ledger's `harvest` revenue and cost deltas to the krone, for the planned order (2 batches) and for the express harvest; the histogram sums to the batches' gutted kilos. |
| 2 | Same-day projection within ±5 % of realized revenue | `HarvestPlannerTests`, player path: 28 of 30 within ±5 %, median 1.09 %, mean -0.11 % (docs/BALANCE.md §1n); UI flow: projected 17,873,202 NOK gross, booked 18,591,607 (-3.9 %); the express quote equals the booking exactly. |
| 3 | "Wait N weeks" uses twin-lite and leaves the live RNG untouched | `TheProjection_LeavesTheLiveGameAndItsRngUntouched`: > 100 projections over 60 days, every SimRng stream and the save unchanged, the probed game equals its twin. |
| 4 | Exactly one toast per harvest | UI tests: a planned order with 2 slaughter days gives one toast and one `harvest_announced`; an express harvest one; the legacy `harvest_pen` verb (heard through two signals) one. A real click on that toast opens the harvest's report. |

## The presentation beat

- Farming Simulator 25 puts each product's price per outlet, with its range
  over the season, in its price menu where the sale is decided
  (modland.net, "Overview of the Price Menu in Farming Simulator 25", 2025).
  The planner puts each size class's price and what waiting would bring next
  to the order button.
- Anno 117 keeps income a top-bar total with its breakdown a click away
  (intoindiegames.com, "Anno 117 Pax Romana money and economy guide", 2025).
  The payoff here is the figure first: one toast with the net, the HUD
  balance counting up, the harvest stinger; the report (net tallying up and
  settling, the bars growing in) is a click on the toast.
- Reduced motion shows every figure whole at once (UI test).
