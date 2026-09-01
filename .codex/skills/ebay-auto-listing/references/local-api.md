# Local Product Resolution

Resolve the product through the repository's current database, product services, configuration, and storage implementation. Do not guess a directory from the visible title alone.

1. Search using the identifier supplied by the user: product ID, SKU/custom label, BID code, original item number plus SKU ID, or title.
2. Require exactly one matching product.
3. Load the full product record, inventory by location, saved price, notes, and image metadata.
4. Follow the configured product image/storage path to the exact directory.
5. Confirm that the directory belongs to the resolved product before reading or writing files.

Prefer existing application services or read-only database access. The development API commonly uses `http://localhost:5077/api`, but resolve the actual URL from project configuration. If the site is not running, inspect the local database and storage configuration directly.

Create a non-destructive output folder such as `ebay-draft-YYYYMMDD-HHMMSS` inside the verified product directory. Copy or derive outputs there; leave all source files unchanged. Save `listing-draft.md` plus processed images with stable numbered names.
