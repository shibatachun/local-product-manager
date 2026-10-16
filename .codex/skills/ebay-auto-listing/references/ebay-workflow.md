# Manual eBay Draft Package

## Image set

- By default, create **no replacement product or hero photos**. The seller will generate and place those separately. Inspect original and seller-supplied images and list their existing full paths in a proposed upload order.
- Put the clearest card-free real product photo first when available. Use existing secondary views to show actual quantity, construction, backs, labels, condition, or scale. Do not imply a backing/price card is included.
- Call out a missing or unsuitable view rather than generating a substitute. Existing AI-edited/generated files may be considered only if genuinely useful and clearly identified as such; they are not mandatory.
- For craft eyes that require glue, state the adhesive requirement in the copy, without suggesting that glue is included. Do not add text to an image unless explicitly requested.
- For brooches, first make source-verified measurement infographics when each item's labeled height/width and suitable front image are available. Follow `brooch-infographic.md`; leave unsupported ones pending. Then generate exactly two distinct AI use-scene illustrations using the `imagegen` skill, each visibly headed **Example of Use**. Keep the brooch's identity and visible design faithful, show a plausible use, and do not imply AI scene props are included. Save both final images in the eBay draft folder and put them after real product photos and infographics in the proposed order. No other image edits are routine. Keep originals untouched; never recycle Walmart-generated images.

## Copy package

Produce these fields in `listing-draft.md`:

1. Product identity, local product SKU, and the separate eBay custom label using the exact local inventory number (`INV-xx`).
2. eBay title of no more than 80 characters.
3. Suggested leaf category plus a manual-verification note.
4. Condition selection and factual condition description. For brooches use the seller's standard **New without tags / Condition ID 1500**. For other product types use **New / 1000** when supported, unless product-specific evidence or user instructions indicate another condition. Flag contrary evidence rather than selecting an inaccurate condition.
5. Item specifics as a simple field/value table, with uncertain values marked `Confirm`.
6. Customer-facing description in readable Markdown and eBay-safe HTML, following the structured description template below.
7. Stored local price or the website Pricing result, available quantity, and inventory location for internal reference. When calculating, use recorded cost and weight plus the Pricing feature's eBay category, logistics, packaging, fee, margin, buffer, and live exchange-rate defaults. Set the final listing price to `floor(raw price) + 0.99`; for example, 12.01, 12.54, and 12.99 all become 12.99. Include the raw calculation result for auditability.
8. Package contents based only on verified evidence.
9. Shipping, handling, and return-policy placeholders when account policies are unknown.
10. Proposed numbered upload order referencing existing photos, newly annotated infographics, and two AI **Example of Use** illustrations, distinguishing all three. Cite each measurement's exact source in the draft's internal notes.
11. A final unresolved-fields checklist.

The user performs the final category selection, policy selection, review, and upload in Seller Hub. Do not describe the draft as published.

## Description format

Write the listing description in this order:

1. Keep two concise opening prose paragraphs. Paragraph 1 identifies the product and its strongest verified visual or functional characteristics. Paragraph 2 states what the buyer receives and directs buyers to the real photos for exact color, finish, condition, construction, and scale. If generated Example of Use images are selected, explain that they are AI styling illustrations only, not photographs of the delivered item.
2. Add `Package Includes` with bullets for verified package count, pieces or pairs, assortment behavior, colors, shape, and included accessories. Make the primary count visually prominent.
3. Add `📏 Size & Material` with verified measurements, material, finish, weight, and construction. Omit unsupported facts; never infer exact dimensions or material from appearance alone.
4. Add `✨ Features` with two to five factual characteristics supported by the record or real photographs. Use conservative language and do not invent performance claims.
5. Add `🎨 Wide Applications`, followed by `Ideal for:`, with plausible uses supported by the product type. Do not suggest unsafe, incompatible, regulated, or unsupported uses.

Use headings, horizontal separators between the latter sections, bullet lists, and selective bold emphasis:

```html
<p>Opening product paragraph.</p>
<p>What the buyer receives and photo/reference notice.</p>
<h2>Package Includes</h2>
<ul><li><strong>40 pieces = 20 pairs</strong></li><li>Color: <strong>Mixed</strong></li></ul>
<hr>
<h2>📏 Size &amp; Material</h2>
<ul><li>Diameter: <strong>20 mm</strong></li><li>Material: <strong>Glass cabochon</strong></li></ul>
<hr>
<h2>✨ Features</h2>
<ul><li><strong>Flat back</strong> — easy to apply</li></ul>
<hr>
<h2>🎨 Wide Applications</h2>
<p>Ideal for:</p>
<ul><li>DIY crafts and handmade projects</li></ul>
```

Keep the HTML portable: use only paragraphs, headings, unordered lists, list items, strong emphasis, line breaks when necessary, and horizontal rules. Do not use CSS, JavaScript, external images, tracking, tables, or unsupported claims. The Markdown description and CSV HTML description must contain the same substantive facts and section order. Omit an unsupported bullet or subsection rather than filling it with `Unknown` or guessed content.

## Seller Hub Draft CSV

Always create `ebay-draft-upload.csv` alongside `listing-draft.md`, using `../assets/eBay-draft-listing-template-US.csv` as the structural source.

Preserve these exact columns and their order:

1. `Action(SiteID=US|Country=US|Currency=USD|Version=1193|CC=UTF-8)` — `Draft`.
2. `Custom label (SKU)` — always use the resolved product's exact local inventory number (`INV-xx`). Never substitute Actual SKU, product SKU, UUID, supplier code, or a temporary filename.
3. `Category ID` — use a verified US leaf-category ID. If uncertain, flag it for confirmation rather than guessing.
4. `Title` — use the same verified title as the Markdown, no more than 80 characters.
5. `UPC` — use the recorded UPC; otherwise leave blank. Never fabricate one.
6. `Price` — final calculated USD listing price with two decimal places and no currency symbol.
7. `Quantity` — current sellable inventory as a whole number.
8. `Item photo URL` — leave blank by default because the user will add selected existing local images after the row reaches eBay Drafts. Only populate a verified public HTTPS image URL.
9. `Condition ID` — use `1500` (New without tags) for brooches; for other product types default to `1000` (New) when supported by the selected category. Flag contrary product evidence for review. Do not put condition text in this column.
10. `Description` — use the complete structured HTML version of the Markdown description: retain the two opening paragraphs and include every supported Package Includes, Size & Material, Features, and Wide Applications section. Escape/quote it as CSV data; exclude scripts, CSS, external tracking, and unsupported claims.
11. `Format` — `FixedPrice` unless the user explicitly requests an auction.

Keep the four official `#INFO` rows above the header. Remove the bundled sample product row. Quote fields according to CSV rules, especially descriptions containing commas, quotes, or line breaks; double embedded quotes. Save as UTF-8. Before delivery, parse the generated file and verify every row has exactly 11 fields.

The CSV creates a draft, not a live listing. Tell the user to upload it in Seller Hub Reports, open the resulting draft, add the selected existing photos, confirm category/condition/item specifics/policies, and publish manually.
