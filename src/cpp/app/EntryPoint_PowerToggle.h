#pragma once

namespace imeindicator::app {

// `/powertoggle` 引数で起動された場合のショートカット処理。
// メインインスタンスが起動中なら IPC Event を立てて即終了。
// 起動していない場合はその場で電源モードをトグルし、バルーン通知を出して終了する。
// 戻り値は WinMain の終了コード。
int runPowerToggleEntry();

} // namespace imeindicator::app
