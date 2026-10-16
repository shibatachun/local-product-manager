---
name: walmart-auto-listing
description: Build a Walmart Marketplace US listing draft from a Local Product Manager product. Use when the user names an INV, SKU, or product and wants compliant Walmart copy, verified item attributes, the saved Marketplace retail price, real white-background product photos, and accurate generated use images. Produce one Markdown draft plus images; never submit or publish unless separately authorized.
---

# Walmart Marketplace Listing Draft

Build a self-contained Walmart US listing package from one verified Local Product Manager product. The normal deliverable is one UTF-8 Markdown file, one self-contained `walmart-copy-ready.html`, and processed images inside a new `walmart-draft-YYYYMMDD-HHMMSS` subfolder of that product's image directory. Do not create CSV, JSON, XML, or API-feed files unless the user separately requests them.

Walmart requirements change. Before preparing each listing, verify the current US Marketplace product identifier policy, image rules, prohibited-products rules, product-details policy, taxonomy, and the exact Product Type specification using official Walmart Marketplace or Developer documentation. Treat the current Product Type specification as authoritative for required and conditional attributes; never reuse a remembered schema when a current one can be retrieved.

## Product resolution

1. Resolve the supplied INV number, SKU, product ID, or title to exactly one local product. Ask only if ambiguity remains.
2. Load the full product record, extended data, Note, current inventory, storage locations, cost, weight, dimensions, package quantity, country of origin, compliance fields, product identifiers, listings, and every original photo through the repository's current API/configuration. Do not guess data from a filename.
3. Verify that the selected product image directory belongs to that exact product before creating the output subfolder. Never alter source images.

## Choose the Walmart setup path

Determine and record one of these paths:

- **Offer Setup by Match:** use only when a verified identifier/catalog search establishes that this exact product already exists in Walmart's catalog. Do not attach the offer to a merely similar product.
- **Full Item Setup:** use when the exact product is not already present. Obtain the current taxonomy and specification for the selected Product Type.
- **Blocked:** use when identity, Product Type, required compliance information, or identifier status cannot be established safely.

The offline Markdown draft is always allowed. Walmart Seller Center supports saving a single-item setup with **Save as Draft**, normally retained for up to 180 days. If the user explicitly asks to place the result in Seller Center and a signed-in browser is available, fill the single-item form and select **Save as Draft** only. Never select **Submit**, call Bulk Item Setup, create an offer, publish, or modify live price/inventory without a separate explicit request. If Save as Draft is unavailable, stop with the offline package instead of intentionally omitting data to force an unpublished state.

## Product identifier rules

- Accept only a product identifier already assigned to this exact selling unit: GTIN-14, UPC/GTIN-12, EAN/GTIN-13, or an appropriate ISBN.
- Validate numeric length and GS1 check digit, but recognize that a mathematically valid number is not proof of ownership or GS1 registration.
- Confirm that the identifier, brand, product, package count, and selling unit agree. A different quantity or bundle generally requires its own correctly assigned identifier.
- Never use an INV number, SKU, model number, another product's identifier, or a random/generated prefix as a Walmart Product ID.
- An EAN may be completed from 12 base digits only when the GS1 prefix is legitimately licensed to the seller and the item reference has been allocated to this product. Calculate the thirteenth digit with the GS1 modulo-10 algorithm and store the allocation so it cannot be reused.
- If the product has no valid identifier, mark **GTIN exemption required**. An exemption must match the approved brand and Product Type exactly. Do not represent an application or draft request as approved.

## Content and attribute preparation

1. Build the required and conditionally required fields from the current Product Type specification.
2. Use only facts supported by the local record, the real product photos, the exact Walmart catalog match, or user-supplied evidence.
3. Prepare an accurate product name, brand, site description, key features, selling-unit/package quantity, count and unit, color, material, dimensions, weight, country of origin, condition, fulfillment information, warnings, and compliance attributes when applicable.
4. For each verified millimeter measurement, show metric and imperial together in Walmart-facing text as `xx mm / yy in` (for example, `18 mm / 0.71 in`). Use this in the item name, site description, key features, size/diameter fields, size infographic, Markdown, and copy-ready HTML. Calculate `in = mm / 25.4`, normally rounded to two decimals. If a Seller Center attribute accepts only one unit, give it the required numeric value/unit separately without changing the dual-unit customer copy. Do not rewrite SKU/model identifiers containing `mm`.
5. Show **item weight** separately from **shipping/package weight**. For this seller's shipping estimate, take the recorded item weight in ounces, add **1 oz packaging allowance**, then round the total **up to the next whole ounce**. Show the result as `xx oz / yy lb` in both deliverables and provide a pounds-only copy value for Walmart's **Package Weight (lb)** field; for example, `3.79 oz item + 1 oz packaging = 4.79 oz → 5 oz / 0.3125 lb`. Do not instead round the item weight before adding packaging. Preserve enough pound precision to avoid understating weight. If a measured complete packed weight is available, use it instead of the estimate (round up to the next whole ounce for the Seller Center value). Clearly label estimated versus measured packed weight. The pricing calculator's historical packaging assumption is not evidence of actual packed weight.
6. Keep brand spelling identical to the physical product/GS1 or approved exemption. Use `Unbranded` only when accurate and accepted for that Product Type; do not substitute a seller name as brand without evidence.
7. Do not make unsupported safety, age, compatibility, health, environmental, performance, certification, or "Made in USA" claims.
8. Separate every field into **Confirmed**, **Needs confirmation**, **Missing required**, or **Not applicable**. A required unknown blocks readiness; never fill it with a guess.
9. Check internal consistency across title, package count, item attributes, description, images, weight, price, and Product ID.

## Pricing

Read the Walmart **Retail price** from the resolved product's **Marketplace listings** section and use that saved value whenever it is greater than zero. Treat it as authoritative because it contains the user's accepted result from **Calculate price**. Record the product page and saved Walmart listing record as the price source; do not replace the saved price by independently rerunning or reproducing the pricing formula.

If the Walmart Retail price is missing or zero:

1. Bind the resolved inventory product so recorded product parameters populate the Pricing form.
2. Select **Walmart**, the closest supported category, and the correct fulfillment/logistics mode.
3. Use recorded cost, weight, dimensions, and available package information. Retain current site configuration only for values the product record does not supply, and identify those defaults in the audit.
4. Click **Calculate price**, save the result to the product's Marketplace listings, then reread and use the saved Walmart Retail price.
5. If neither a valid saved price nor a successful calculation is available, mark pricing incomplete instead of inventing a value.

When the product page exposes detailed calculation output, include it in the Markdown pricing audit. When only the saved Retail price is available, record the saved price and its source without reconstructing unavailable fee, shipping, margin, or profit values.

## Images

Inspect every source photo before selecting or editing it. Exclude unrelated, duplicate, corrupt, severely blurred, or wrong-product files.

### Processed real images

- Create at least one strong real product-only image. Use the best card-free source; do not place a removable label, price, backing, or packaging card behind the product when a usable card-free source exists.
- The main image must be a real image of the exact item and exact included quantity, never an AI-generated scene.
- Replace the original backdrop when it is gray, off-white, textured, bounded, or shadowed. White padding around an unchanged background is not background removal.
- Output square 1:1, RGB, 8-bit JPG or PNG images. Target 2200 × 2200 pixels when supported by the source; require at least 1500 × 1500 for zoom and keep each file at or below 5 MB.
- The main background must be seamless pure white, approximately RGB 255/255/255. Center and crop the product closely without touching the frame.
- No watermark, seller name, logo, retailer branding, promotional wording, borders, or claims. Do not show accessories, quantities, colors, or packaging that the buyer will not receive.
- Preserve real shape, count, proportions, surface, markings, transparency, reflections, defects, and included parts. A useful hand-for-scale photo may remain secondary but cannot replace the clean product-only main image. The hand photo may show a clearly disclosed representative subset with fewer pieces than the full selling unit when that makes the scale and product easier to see; never present that subset as the complete package quantity.
- When the main image shows many repeated loose pieces, align them into straight, visually balanced rows or columns with consistent spacing. Preserve the exact included count and assortment, do not add or remove pieces except to correct a photographed overcount to the verified selling-unit count, and visually verify that no item is duplicated, clipped, crooked, or left outside the arrangement.
- For repeated loose-piece products, also require a secondary real loose/scattered view whenever such a source photo exists. Replace only its background with seamless white and preserve the source pieces in their photographed positions; do not generate a new scatter, randomly swap variants, or reorganize the source. Keep the exact count and the same set of existing variants.
- Also require a real hand-for-scale/reference image whenever such a source photo exists. Retain the hand, anatomy, product contact, and photographed product positions; replace only the surrounding backdrop with seamless white. The hand image is secondary and must not replace the clean product-only main image. If either the scattered or hand source view was not photographed, state that it is unavailable rather than fabricating it.

For the hand-for-scale image, use this editing prompt, substituting only product-specific nouns when needed:

> Edit the supplied real product photo into a square marketplace hand-scale image. Precisely segment and retain the original hand and every photographed product piece, including the natural skin texture, finger anatomy, overlaps, contact points, exact product designs, count, orientations, and relative positions. Remove the tabletop and all background texture completely, replacing it edge-to-edge with seamless pure white (#FFFFFF). Crop and reframe closely so the hand and products fill most of the square, allowing the hand or wrist to enter naturally from the left or lower edge. Do not leave the original rectangular photo, gray cast, paper texture, padding seam, large unused margins, or border. Do not redraw, beautify, reshape, move, straighten, regroup, duplicate, remove, or invent the hand or products. Preserve realistic edges and contact shadows only; add no text, props, graphics, or extra objects.

Reject the result if it merely places the original photo on a white square, leaves a gray/off-white tabletop, creates white sidebars, shrinks the subject excessively, changes anatomy, or rearranges any piece.

### Size infographic

Create exactly one additional square size infographic from a verified real view of one individual eye, component, or sold item. Isolate that exact object cleanly and show a simple measurement line or dimension marker using only recorded product dimensions. The primary dimension label must include both metric and imperial values in this exact order: `xx mm / xx in` (for example, `12 mm / 0.47 in`), consistent with the rest of the customer-facing copy. Keep text minimal, factual, large, and legible; verify every character and conversion after generation. Do not infer thickness, length, width, diameter, or scale from the photo when it is not recorded. If no trustworthy dimension is available, mark the infographic blocked instead of inventing one. The infographic is secondary and can never be the main image.

### Generated reference images

Use the `imagegen` skill to create exactly two additional reference-based lifestyle/use images from the strongest card-free real product photos, in addition to the one required size infographic. Generated images must:

- Keep the actual product visually faithful in size, shape, color, material, count behavior, and intended use.
- Show only plausible settings or uses supported by the product type.
- Avoid unsupported accessories, performance claims, dangerous use, misleading scale, text, logos, packaging, or a different product variant.
- Remain secondary; never use either generated image as the Walmart main image.
- Be named clearly as generated reference images in the local manifest and Markdown.
- Export final AI-generated scenes as standard 24-bit RGB JPEG by default (no alpha channel), with JPEG quality high enough for detail but a verified final file size below 5 MB. Do not upload the raw ARGB/RGBA PNG returned by an image generator when it exceeds Walmart's limit.
- Be newly generated Walmart-specific assets for the current Walmart draft. Never copy, hard-link, or reuse a generated image from an eBay or other marketplace draft for the same product.
- Use materially different supported use cases and compositions from the product's eBay generated images. Renaming, resizing, recompressing, color shifting, or lightly recropping an eBay image does not make it distinct.

Before delivery, compare the Walmart generated files against sibling marketplace draft folders using filenames, visual inspection, and SHA-256 hashes. Any identical file is a failure. Regenerate it with a genuinely different, product-appropriate scene and composition while keeping the product faithful. Also reopen every final upload file and verify its extension matches its actual encoding, dimensions meet the current Walmart minimum, color mode is RGB without alpha, and byte size is below Walmart's current limit. Treat this final exported file—not the generator's source—as the upload artifact.

Name files in upload order, for example:

```text
01-main-real.png
02-real-detail.png
03-real-scale.png
04-size-infographic.png
05-generated-reference-use.png
06-generated-reference-lifestyle.png
```

Reopen and visually inspect every processed and generated image. Confirm both Walmart-generated scenes are materially different from every eBay-generated scene for the same product. Confirm the main background is genuinely white and its repeated pieces are evenly aligned; confirm the secondary scattered and hand views preserve their respective source arrangements while replacing only the background; verify the count and product identity, ensure no source rectangle or padding seam remains, confirm the infographic shows one exact item with an accurate `mm / in` measurement, and ensure generated scenes do not misrepresent what is included. Redo any failing image before delivery.

## Markdown deliverable

Create exactly one `walmart-listing-draft.md` containing:

1. **Readiness:** `READY FOR SELLER CENTER DRAFT`, `READY FOR USER REVIEW`, or `BLOCKED — MISSING REQUIRED DATA`.
2. **Product identity:** local product ID, INV, SKU, product name, source directory, quantity, and Note.
3. **Setup path:** Offer Setup by Match or Full Item Setup, with evidence and uncertainty.
4. **Identifier audit:** identifier type/value, length/check-digit result, ownership/match evidence, or GTIN-exemption requirement.
5. **Product Type and categorization:** current Walmart Product Type, category path, specification/version/date, and fields requiring Seller Center confirmation.
6. **Copy-ready content:** product name, site description, and concise key features containing only verified claims.
7. **Attributes table:** every relevant required, conditional, and recommended attribute with value, evidence source, and status.
8. **Compliance table:** country of origin, warnings, batteries/powder/liquid/magnet status, age restrictions, Prop 65 or other applicable fields, with unknown requirements clearly blocked.
9. **Offer information:** condition, calculated price, available sellable quantity, fulfillment method, recorded item weight, the `+1 oz` packaging calculation or measured packed weight, `xx oz / yy lb` shipping-weight display, the exact pounds-only number for Walmart's **Package Weight (lb)** field, tax/shipping-policy placeholders, and anything requiring account-level selection.
10. **Pricing audit:** complete Local Pricing output and any defaulted inputs.
11. **Numbered image order:** distinguish processed real images, the one size infographic, and the two generated reference images; state that only the real product-only image is eligible as the main image.
12. **Final Walmart validation:** required fields, image checks, Product ID, copy consistency, prohibited claims/products, and unresolved blockers.
13. **Draft instructions:** tell the user how to create/open the single item in Seller Center and choose **Save as Draft**. State clearly that the package is not submitted or published.

Do not generate empty companion files. Put all audits, copy, attributes, image manifest, and instructions in the Markdown document.

Always create `walmart-copy-ready.html` beside the Markdown so the user can transfer the draft into Walmart Seller Center without extracting fields manually. **Use [assets/walmart-copy-ready.template.html](assets/walmart-copy-ready.template.html) as the starting point for every Walmart HTML draft.** If this asset is unavailable, use the existing INV-63 copy-ready HTML at `LocalProductManager.Api/Data/product-images/INV-63/walmart-draft-20260922-133706/walmart-copy-ready.html` as the visual reference and rebuild the same structure. The asset captures the INV-63 layout that the user chose: compact hero and readiness badge; Product name, Site description, and one complete Key features list as the first three cards; a two-column Seller Center attribute-card grid; numbered image gallery; and a separate blocker card. Retain its responsive styling and clipboard behavior. Do not revert to the INV-67-style page of individual read-only inputs/textareas or five separate feature-copy controls. Replace every template token with this product's verified or clearly labeled pending information, HTML-escape inserted text, and verify no tokens or another product's facts remain. Render each attribute as a `.attr` card with a unique value-element `id` and matching button `data-copy`; render each image as a gallery `figure` with a local `img` and caption. Adapt card counts to the actual product without changing the overall layout.

Give the item name and description their own copy buttons. Give the complete Key Features list one copy button for Walmart's single rich-text field. When rich clipboard support is available, write an actual `text/html` ClipboardItem containing the list **and** a readable `text/plain` bulleted version; never call `writeText` with literal `<ul>`/`<li>` markup. If rich clipboard access fails in a local `file://` page, copy newline-separated plain-text bullets instead. Check the clipboard/fallback result and show success only when copying succeeds. Do not force individual copying of each feature.

Below the customer copy, include a clearly labeled, copyable **Seller Center attributes** section. Populate each verified applicable field separately—at minimum item diameter/size, item weight, package/shipping weight in oz and lb, pounds-only Package Weight (lb), package count, material, color, shape, country of origin, condition, price, and quantity when known. Include other Product Type attributes supported by the record; mark unknown or conditional fields as needing confirmation rather than inventing values. For a unit-specific Walmart field, expose the exact value to paste, not only a human-readable composite. Keep readiness warnings, identifier status, internal audits, and image instructions visibly separate from these controls. Apply the dual-unit measurement rule to customer-facing values. The HTML must be responsive and self-contained, with only local draft images/Markdown linked. Reopen it in a browser to verify rendering and copy-button behavior when permitted; if the local browser blocks access, do not bypass that restriction—validate script syntax and local references, then report that browser interaction remains unverified.

## Safety boundaries

- Never publish or submit as part of ordinary skill use.
- Never use Bulk Item Setup API as a draft mechanism; official feed submission begins Walmart's item-ingestion/publishing workflow.
- Never modify a live Walmart listing, offer, price, or inventory without explicit authorization.
- Never fabricate or recycle GTIN/UPC/EAN values.
- Never bypass required fields by deliberately submitting incomplete data.
- Never mix facts or images across SKUs.
- Never overwrite original photographs.
- Never describe a Seller Center draft as live or approved.

## Final response

Return the product identity, readiness, output folder, `walmart-listing-draft.md`, `walmart-copy-ready.html`, numbered image files, suggested Product Type, setup path, identifier status, calculated price, blockers, and concise Save-as-Draft instructions. Distinguish real images, the size infographic, and generated reference images. State explicitly that no listing was submitted or published.
