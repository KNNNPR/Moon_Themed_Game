# MoneyGetApp / ДенежноеДрево

Buy the app once in SHOP for 40 CR. Its desktop icon uses the existing growth-report SVG; the store preview uses Apple.

Select a company, set shares and leverage, then choose LONG (profit when the price rises) or SHORT (profit when it falls). One position per company; close it before changing direction. CLOSE realizes the result; CLOSE ALL settles the whole portfolio.

The wallet reserves `ceil(price * shares / leverage)` credits as collateral. Closing returns collateral plus profit/loss, rounded down to whole credits. A short sale does not give spendable sale proceeds. There are no fees, debt or negative payouts. Liquidation loses the collateral, including if a quote jumps past the liquidation price.

Optional SL and TP apply to the new position: stop-loss at -20% of collateral, take-profit at +35%. They execute against the next quote, so a price jump can overshoot those levels. These controls do not modify existing positions.

Prices update every three seconds, including while the application is closed. Settings pause the market with the rest of the game. Ownership, positions and prices last for the current play session, matching the PC wallet.

Edit **Assets/Resources/MoneyGetContent.asset** in the Inspector for the purchase price, quote interval, company names, opening prices, volatility and English/Russian news. The exchange is a fictional game simulation.
