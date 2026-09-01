# Manual eBay Draft Package

## Image set

- Export square images at a consistent high resolution suitable for eBay. Prefer 1600 x 1600 pixels when the source supports it; never upscale a weak source in a way that invents detail.
- Use a genuinely white background for every processed real photo. The pixels surrounding the product should be near pure white (approximately RGB 248–255) without a visible gray/off-white source-photo rectangle.
- White padding used to make a portrait photo square is only layout work; it is never evidence that the original background was removed. Crop and replace/normalize the source background first, then create the square composition.
- Do not use a simple brightness, saturation, or neutral-color threshold when the product has glass reflections, pale details, white backs, transparent edges, or a hand: those pixels can be mistaken for background. Use subject-aware background extraction or a tightly constrained image edit, then verify the product count, reflections, pale details, hand anatomy, and edges against the source.
- Reopen each final real image after export and look for background boundaries, gray casts, floor/table texture, uneven corners, and padding seams. Also run `scripts/validate_marketplace_images.py` against the real-image files. For an intentional hand-for-scale photo whose hand touches the frame, validate it separately with `--allow-subject-at-border --min-white-ratio 0.40`; never use that exception for the hero image or to excuse a visible old backdrop. Do not deliver an image set that fails either the visual review or the applicable validator mode.
- Rank card-free source photos first. Exclude photos with a card, label card, backing card, price card, or packaging card behind the product whenever a usable card-free angle of the same product is available.
- Center the item with comfortable margins and preserve realistic shadows when helpful.
- Keep a visible hand if it provides scale or demonstrates handling. Clean the background around it without changing anatomy or product contact.
- Make image 01 the strongest card-free real, product-only hero image. A card-backed source must not be the hero image when any usable card-free source exists.
- Include useful real angles, labels, details, included pieces, and visible condition evidence.
- Generate exactly two secondary reference images showing plausible use or scale. Prefer card-free photos as generation references, preserve distinctive geometry and markings, and avoid unsupported accessories or claims.
- Use a card-backed photo only as a last-resort secondary evidence image, or when the card is verified as an included part of the product. Do not assume a removable background card is included.
- Name real edits `01-main-real.jpg`, `02-real.jpg`, and so on. Name generated files at the end as `NN-generated-reference-1.jpg` and `NN-generated-reference-2.jpg`.

## Copy package

Produce these fields in `listing-draft.md`:

1. Product identity and local SKU/custom label.
2. eBay title of no more than 80 characters.
3. Suggested leaf category plus a manual-verification note.
4. Condition selection and factual condition description.
5. Item specifics as a simple field/value table, with uncertain values marked `Confirm`.
6. Customer-facing description in readable Markdown and eBay-safe HTML, following the structured description template below.
7. Stored local price or the website Pricing result, available quantity, and inventory location for internal reference. When calculating, use recorded cost and weight plus the Pricing feature's eBay category, logistics, packaging, fee, margin, buffer, and live exchange-rate defaults. Set the final listing price to `floor(raw price) + 0.99`; for example, 12.01, 12.54, and 12.99 all become 12.99. Include the raw calculation result for auditability.
8. Package contents based only on verified evidence.
9. Shipping, handling, and return-policy placeholders when account policies are unknown.
10. Numbered photo upload order, explicitly marking the two generated reference images.
11. A final unresolved-fields checklist.

The user performs the final category selection, policy selection, review, and upload in Seller Hub. Do not describe the draft as published.

## Description format

Write the listing description in this order:

1. Keep two concise opening prose paragraphs. Paragraph 1 identifies the product and its strongest verified visual or functional characteristics. Paragraph 2 states what the buyer receives and directs buyers to the real photos for exact color, finish, condition, construction, and scale. State that generated lifestyle images show styling or use possibilities only when such images exist.
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
2. `Custom label (SKU)` — prefer Actual SKU; otherwise use the stable internal product SKU/UUID. Never use a temporary filename.
3. `Category ID` — use a verified US leaf-category ID. If uncertain, flag it for confirmation rather than guessing.
4. `Title` — use the same verified title as the Markdown, no more than 80 characters.
5. `UPC` — use the recorded UPC; otherwise leave blank. Never fabricate one.
6. `Price` — final calculated USD listing price with two decimal places and no currency symbol.
7. `Quantity` — current sellable inventory as a whole number.
8. `Item photo URL` — leave blank by default because the user will add processed local images after the row reaches eBay Drafts. Only populate a verified public HTTPS image URL.
9. `Condition ID` — use a verified numeric eBay condition ID appropriate to the selected category. Do not put condition text in this column.
10. `Description` — use the complete structured HTML version of the Markdown description: retain the two opening paragraphs and include every supported Package Includes, Size & Material, Features, and Wide Applications section. Escape/quote it as CSV data; exclude scripts, CSS, external tracking, and unsupported claims.
11. `Format` — `FixedPrice` unless the user explicitly requests an auction.

Keep the four official `#INFO` rows above the header. Remove the bundled sample product row. Quote fields according to CSV rules, especially descriptions containing commas, quotes, or line breaks; double embedded quotes. Save as UTF-8. Before delivery, parse the generated file and verify every row has exactly 11 fields.

The CSV creates a draft, not a live listing. Tell the user to upload it in Seller Hub Reports, open the resulting draft, add the numbered local images, confirm category/condition/item specifics/policies, and publish manually.
