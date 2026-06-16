# Local network access

Start the backend:

```powershell
cd LocalProductManager.Api
dotnet run --launch-profile http
```

Build the Angular frontend after changing UI files:

```powershell
cd LocalProductManager.Web
npm.cmd run build
```

Find the host computer's IPv4 address:

```powershell
ipconfig
```

The .NET backend serves the built Angular application. Other computers on the same network can open:

```text
http://HOST_IP:5077
```

For example:

```text
http://192.168.1.25:5077
```

The frontend automatically connects to the API at:

```text
http://HOST_IP:5077/api
```

For Angular live development, `npm.cmd start` still exposes port `4200`.

If Windows Firewall prompts for access, allow the .NET application on private networks.

## Shared product images

The server can read images from another computer and publish them through the website.
Configure the folders that the server is allowed to expose in
`LocalProductManager.Api/appsettings.json`:

```json
"ImageStorage": {
  "AllowedRoots": [
    "\\\\IMAGE-PC\\ProductImages"
  ]
}
```

Use a UNC network path, not a drive letter mapped only for one interactive Windows user.
The Windows account running the .NET server must have read permission for that share.

Clients can then access a product image through:

```text
http://HOST_IP:5077/api/products/PRODUCT_ID/image
```
