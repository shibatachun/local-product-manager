#[tauri::command]
async fn check_for_updates(app: tauri::AppHandle) -> Result<String, String> {
    use tauri_plugin_updater::UpdaterExt;

    let updater = app.updater().map_err(|error| error.to_string())?;
    match updater.check().await.map_err(|error| error.to_string())? {
        Some(update) => {
            update
                .download_and_install(|_, _| {}, || {})
                .await
                .map_err(|error| error.to_string())?;
            app.restart();
        }
        None => Ok("LocalProductManager is up to date.".to_string()),
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_updater::Builder::new().build())
        .invoke_handler(tauri::generate_handler![check_for_updates])
        .setup(|app| {
            use tauri::Manager;
            use tauri_plugin_updater::UpdaterExt;

            if let Some(window) = app.get_webview_window("main") {
                let title = format!(
                    "LocalProductManager v{}",
                    app.package_info().version
                );
                window.set_title(&title)?;
            }

            let handle = app.handle().clone();
            tauri::async_runtime::spawn(async move {
                let updater = match handle.updater() {
                    Ok(updater) => updater,
                    Err(error) => {
                        eprintln!("Unable to initialize updater: {error}");
                        return;
                    }
                };

                match updater.check().await {
                    Ok(Some(update)) => {
                        println!("Installing LocalProductManager update {}", update.version);
                        match update.download_and_install(|_, _| {}, || {}).await {
                            Ok(()) => handle.restart(),
                            Err(error) => eprintln!("Unable to install update: {error}"),
                        }
                    }
                    Ok(None) => println!("LocalProductManager is up to date"),
                    Err(error) => eprintln!("Unable to check for updates: {error}"),
                }
            });

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running LocalProductManager desktop app");
}
