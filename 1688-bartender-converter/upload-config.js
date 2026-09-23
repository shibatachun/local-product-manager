// Configure this after the cloud receiver is deployed.
// The endpoint must accept multipart/form-data with a CSV file in the `file` field.
window.CONVERTER_UPLOAD_CONFIG = Object.freeze({
  endpoint: "http://134.175.148.65/csv-inbox/upload",
  authorizationToken: "",
});
