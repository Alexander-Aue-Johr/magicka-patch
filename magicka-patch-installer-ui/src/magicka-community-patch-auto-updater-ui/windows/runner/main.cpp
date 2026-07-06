#include <flutter/dart_project.h>
#include <flutter/flutter_view_controller.h>
#include <windows.h>

#include "flutter_window.h"
#include "utils.h"

namespace {

void CalculateInitialWindow(Win32Window::Point* origin,
                            Win32Window::Size* size) {
  constexpr double kUiAspect = 1220.0 / 720.0;
  constexpr double kScreenFill = 0.92;
  constexpr int kMinWidth = 1180;
  constexpr int kMinHeight = 700;

  POINT monitor_point = {0, 0};
  HMONITOR monitor = ::MonitorFromPoint(monitor_point, MONITOR_DEFAULTTOPRIMARY);
  MONITORINFO monitor_info = {};
  monitor_info.cbSize = sizeof(MONITORINFO);

  RECT work_area = {0, 0, 1280, 720};
  if (::GetMonitorInfo(monitor, &monitor_info)) {
    work_area = monitor_info.rcWork;
  }

  const int work_width = work_area.right - work_area.left;
  const int work_height = work_area.bottom - work_area.top;
  int target_width = static_cast<int>(work_width * kScreenFill);
  int target_height = static_cast<int>(target_width / kUiAspect);
  const int max_height = static_cast<int>(work_height * kScreenFill);

  if (target_height > max_height) {
    target_height = max_height;
    target_width = static_cast<int>(target_height * kUiAspect);
  }
  if (target_width < kMinWidth && work_width >= kMinWidth) {
    target_width = kMinWidth;
    target_height = static_cast<int>(target_width / kUiAspect);
  }
  if (target_height < kMinHeight && work_height >= kMinHeight) {
    target_height = kMinHeight;
    target_width = static_cast<int>(target_height * kUiAspect);
  }
  if (target_width > work_width) {
    target_width = work_width;
  }
  if (target_height > work_height) {
    target_height = work_height;
  }

  const int left = work_area.left + (work_width - target_width) / 2;
  const int top = work_area.top + (work_height - target_height) / 2;
  *origin = Win32Window::Point(left, top);
  *size = Win32Window::Size(target_width, target_height);
}

}  // namespace

int APIENTRY wWinMain(_In_ HINSTANCE instance, _In_opt_ HINSTANCE prev,
                      _In_ wchar_t *command_line, _In_ int show_command) {
  // Attach to console when present (e.g., 'flutter run') or create a
  // new console when running with a debugger.
  if (!::AttachConsole(ATTACH_PARENT_PROCESS) && ::IsDebuggerPresent()) {
    CreateAndAttachConsole();
  }

  // Initialize COM, so that it is available for use in the library and/or
  // plugins.
  ::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

  flutter::DartProject project(L"data");

  std::vector<std::string> command_line_arguments =
      GetCommandLineArguments();

  project.set_dart_entrypoint_arguments(std::move(command_line_arguments));

  FlutterWindow window(project);
  Win32Window::Point origin(10, 10);
  Win32Window::Size size(1280, 720);
  CalculateInitialWindow(&origin, &size);
  if (!window.Create(L"Magicka Community Patch Auto Updater", origin, size)) {
    return EXIT_FAILURE;
  }
  window.SetQuitOnClose(true);

  ::MSG msg;
  while (::GetMessage(&msg, nullptr, 0, 0)) {
    ::TranslateMessage(&msg);
    ::DispatchMessage(&msg);
  }

  ::CoUninitialize();
  return EXIT_SUCCESS;
}
