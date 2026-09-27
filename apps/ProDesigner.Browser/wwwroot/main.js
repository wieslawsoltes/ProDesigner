import { dotnet } from './_framework/dotnet.js';
const status = document.querySelector('#boot-status');
try {
  const runtime = await dotnet.withDiagnosticTracing(false).create();
  runtime.setModuleImports('prodesigner-storage', {
    read: key => localStorage.getItem(key), write: (key, value) => localStorage.setItem(key, value), remove: key => localStorage.removeItem(key)
  });
  const config = runtime.getConfig();
  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
  const api = exports.ProDesigner.Browser.Program;
  window.prodesigner = {
    state: () => JSON.parse(api.Snapshot()), bounds: name => JSON.parse(api.Bounds(name)), command: command => api.Command(command),
    select: name => api.Select(name), setProperty: (name, value) => api.SetProperty(name, value),
    exportWorkspace: () => api.ExportWorkspace(), importWorkspace: json => api.ImportWorkspace(json),
    insert: name => api.Insert(name), setSource: source => api.SetSource(source)
  };
  await runtime.runMain(config.mainAssemblyName, []);
  const wait = setInterval(() => {
    if (window.prodesigner.state().ready) { clearInterval(wait); document.querySelector('#boot').remove(); }
  }, 50);
} catch (error) {
  status.textContent = `The workbench could not start: ${error.message}. Reload the page or use the desktop build.`;
  console.error(error);
}
