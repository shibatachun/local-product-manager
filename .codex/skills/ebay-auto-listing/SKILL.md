---
name: ebay-auto-listing
description: Build a complete manual eBay listing draft package from a Local Product Manager product without using the eBay API. Use when the user names a product and wants Codex to locate its product directory, prepare square white-background product photos, preserve a visible hand when it provides scale, generate two accurate reference-based lifestyle images, and produce both a copy-ready Markdown draft and an uploadable eBay Seller Hub Draft CSV containing title, description, category, condition, price, quantity, SKU, and related listing fields. Keep Walmart and Etsy out of scope unless explicitly requested.
---

# eBay Manual Listing Draft

Build a self-contained folder and copy-ready draft that the user can manually upload to eBay. Do not require an eBay Developer account or OAuth.

Treat the local product record, its exact product directory, original photos, inventory, and saved pricing as authoritative. Read `references/local-api.md` before resolving a product or its files. Read `references/ebay-workflow.md` before preparing images or listing copy.

## Workflow

1. Resolve the named product to exactly one local product. Ask only when multiple records remain ambiguous.
2. Load its full record, exact product directory, all original photos, inventory by location, and saved price.
3. Create a draft output subfolder inside the product directory so originals remain untouched.
4. Inspect every source photo before editing. Reject unrelated, duplicate, corrupt, or unusably blurred files. Prefer real photos where no card, label card, backing card, price card, or packaging card appears behind the product. When equivalent card-free photos exist, do not select card-backed photos for processing or as image-generation references.
5. Produce square marketplace images on a genuinely white background. Replacing or extracting the original background is required when it is gray, off-white, textured, shadowed, or visibly bounded; adding white canvas padding around an unchanged photo does **not** count as white-background processing. Start from the strongest card-free source whenever possible. Preserve the real product's shape, color, texture, markings, wear, count, arrangement, and included parts.
6. When a source photo contains a hand that usefully demonstrates scale or use, retain the hand and clean the surrounding background. Do not remove it automatically.
7. Select the strongest card-free real product-only image as the main image. Never use an AI-generated scene as the main image. Never use a photo with a card behind the product as the main image when a usable card-free view exists.
8. Generate exactly two additional reference-based lifestyle/use-context images from the real product photos. Use the `imagegen` skill and provide the strongest card-free source images as references. Keep the product visually faithful and label these files as generated reference images.
9. Derive copy only from the product record, images, and user-provided evidence. Do not infer uncertain brand, material, measurements, compatibility, condition, model, or included accessories as fact.
10. Keep the title within 80 characters and front-load the strongest verified search terms.
11. Use the saved local price when present. Otherwise run the website's Pricing calculation with the product's recorded cost and weight, eBay platform settings, the matching category, and the site's current defaults. Convert the raw result upward to the `.99` price in the same dollar band: any result from 12.00 through 12.99 becomes 12.99, including 12.01 and 12.54. Record both raw and final prices and never substitute marketplace-comparable pricing for this calculation.
12. Produce a complete copy-ready draft and a numbered image-upload order. Save the draft as UTF-8 Markdown in the output folder. Retain two opening prose paragraphs, then use the evidence-driven Package Includes, Size & Material, Features, and Wide Applications structure defined in `references/ebay-workflow.md`.
13. At the same time, create `ebay-draft-upload.csv` from `assets/eBay-draft-listing-template-US.csv`. Preserve its four `#INFO` rows and exact official header. Replace the sample row with exactly one row for the resolved product. Read the CSV rules in `references/ebay-workflow.md` before populating it.
14. Reopen and visually inspect every processed real image, not only the sources. A completed white-background image must have no visible gray/off-white rectangle, original backdrop boundary, room surface, or padding seam around the subject. Run `scripts/validate_marketplace_images.py` on the processed real images; treat its pixel checks as a minimum screen, not a substitute for visual inspection. If either check fails, redo the background before delivery.
15. Validate that the CSV has the unchanged 11-column header, one product row, valid CSV quoting, `Action=Draft`, and UTF-8 encoding. The Markdown and CSV values for title, price, quantity, SKU, condition, and description must agree.
16. Return the output folder, Markdown draft, and uploadable CSV to the user. Do not attempt API publishing.

## Safety Boundaries

- Never overwrite or destructively edit original product photos.
- Never change the actual product, hide defects, fabricate labels, add accessories, or make generated images imply unsupported features.
- Never claim that an AI-generated image is a real photograph. Keep the two generated files distinguishable in filenames and in the draft manifest.
- Never mix images or facts from another SKU. Verify the product identity before producing outputs.
- Do not treat a removable card behind the product as part of the product. Use a card-backed photo only when no usable card-free source exists or when the card is genuinely included with the item; keep it secondary and state the uncertainty if inclusion is unclear.
- Never claim the category or item specifics are certain when the available evidence is insufficient; mark uncertain fields for manual confirmation.
- Never attempt to log in to eBay, publish, or revise a live listing in this manual workflow.
- Treat the bundled CSV as an eBay US Seller Hub Draft upload template. Do not silently invent a Category ID, UPC, or Condition ID. If a required coded value cannot be supported, leave it blank when eBay permits that or mark the package not upload-ready and identify what the user must confirm.

## Output

Return: readiness summary; product identity and source directory; output directory; Markdown draft; Seller Hub Draft CSV; numbered image list distinguishing edited real photos from two generated reference images; title; category suggestion; condition; item specifics; description; price; quantity; SKU/custom label; shipping and return notes; unresolved fields; and concise upload instructions.
