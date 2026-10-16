---
name: ebay-auto-listing
description: Build a manual eBay listing draft from a Local Product Manager product using supplied photos and verified product data. For brooches, create source-verified size infographics followed by two clearly labeled AI Example of Use images.
---

# eBay Manual Listing Draft

Build a self-contained text/CSV draft that the seller can review and upload manually. Do not require an eBay Developer account or OAuth, and never publish through an API.

Treat the current local product record, exact product directory, original and seller-supplied photos, inventory, and saved eBay Retail price as authoritative. Read `references/local-api.md` before resolving a product or its files. Read `references/ebay-workflow.md` for copy and CSV requirements. For brooches with readable size labels, read `references/brooch-infographic.md` before making the infographic.

## Default workflow: supplied photos, brooch infographics, and use examples

1. Resolve the requested identifier to exactly one local product and load its full record, Note, image metadata, inventory by location, and saved eBay listing/price. Verify that the product image directory belongs to that record.
2. Inspect the available original and seller-supplied images. The seller will separately create and place ordinary listing images; use their existing file paths in the proposed upload order. Prefer a clear, card-free real photo for the first image when available, and identify any edited/generated seller-supplied image honestly. Check selling-unit count, identity, and useful front/back or condition views.
3. Do not recreate ordinary listing photos by default: no new hero composites, background replacements, or product-photo edits. The seller supplies those. For brooches, create a measurement infographic when the exact item's height and width are verified (prefer its own labeled photo) and a suitable matching front image exists. If the inputs are missing or ambiguous, flag the infographic as pending; do not invent a picture or measurement.
4. For each supported brooch, read `references/brooch-infographic.md`. Read height and width from that exact item's labeled single-product image by default; use Note or database dimensions only when the image lacks a reliable label and the alternative source is unambiguous. Put the horizontal header **INFOGRAPHIC** across the top. Default to dimension guides on the **front** view only; if a verified matching back view is available, place front left and back right without repeating the guides. Record source filenames, visible label, units, and axis mapping in the audit and draft. Do not infer measurements from pixels, reuse another item's label, or assign bundle weight to one brooch.
5. **After the infographic step**, create exactly **two** separate AI-generated use-scene images for the listing, following the `imagegen` skill. Label each image visibly **Example of Use** and identify it in the manifest and description as an AI styling illustration, not a photograph of the delivered item. Ground the brooch design, count, and plausible wearing/display context in the verified source photos. Do not invent materials, attachments, accessories, or package contents. Keep these images secondary to the real product views and infographic; save final files in the product's eBay draft folder, never only in a generation cache. Do not reuse Walmart images.
6. Derive title, item specifics, package contents, condition description, and buyer-facing copy only from the product record and inspected photos. Do not invent brand, material, stone composition, features, count, or accessories. Use **New without tags / Condition ID 1500** for brooches unless product evidence conflicts; for other products use New / 1000 only when appropriate and supported. Keep the title at most 80 characters.
7. Use the saved eBay **Retail price** from the product's Marketplace listings when positive; this is the seller's accepted Calculate price result. Keep the Markdown and CSV price identical. If it is missing or zero, use the website's Calculate price workflow and reread the saved result. If that cannot be completed, leave pricing clearly incomplete rather than guessing.
8. Create a non-destructive draft folder in the verified product directory containing `listing-draft.md`, `ebay-draft-upload.csv`, supported infographic image/audit files, and two AI Example of Use images. The image manifest must distinguish existing seller images, annotated infographics, and AI illustrations. Use `assets/eBay-draft-listing-template-US.csv` and `references/ebay-workflow.md` for the official four info rows, exact 11-column order, UTF-8 quoting, `Action=Draft`, and eBay custom label equal to the exact `INV-xx` value.
9. Verify title length, field/source consistency, image paths, infographic labels and appearance, Example of Use labels and product fidelity, CSV parseability, and agreement between Markdown and CSV for title, price, quantity, condition, and description. Return the draft folder, Markdown, CSV, proposed photo order, infographics made or pending, both use images, and unresolved checks. The seller chooses policies, reviews photos, and publishes manually.

## Safety boundaries

- Do not overwrite or move original product photos. Do not claim an AI-edited image is an unaltered real photo.
- Never use another SKU's images or measurements, the attached example listing's values, or an inferred package count.
- A removable backing/price card is not an included accessory unless verified.
- Flag uncertain category, UPC, item specifics, inventory, shipping/return policy, or missing price instead of inventing them.
- Keep Walmart and Etsy out of scope unless separately requested.
