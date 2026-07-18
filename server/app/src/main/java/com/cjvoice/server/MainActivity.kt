package com.cjvoice.server

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.localbroadcastmanager.content.LocalBroadcastManager
import com.cjvoice.server.databinding.ActivityMainBinding
import java.net.Inet4Address
import java.net.NetworkInterface

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private var isStreaming = false

    private val statusReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            val status = intent?.getStringExtra(AudioStreamService.EXTRA_STATUS) ?: return
            when (status) {
                AudioStreamService.STATUS_LISTENING -> {
                    binding.statusText.text = getString(R.string.status_listening)
                    binding.startButton.text = getString(R.string.stop_server)
                    binding.clientInfo.text = "等待客户端连接..."
                }
                AudioStreamService.STATUS_STREAMING -> {
                    binding.statusText.text = getString(R.string.status_streaming)
                    val clientIp = intent.getStringExtra(AudioStreamService.EXTRA_CLIENT_IP) ?: ""
                    binding.clientInfo.text = "已连接客户端: $clientIp"
                }
                AudioStreamService.STATUS_STOPPED -> {
                    binding.statusText.text = getString(R.string.status_idle)
                    binding.startButton.text = getString(R.string.start_server)
                    binding.clientInfo.text = ""
                }
                AudioStreamService.STATUS_ERROR -> {
                    binding.statusText.text = getString(R.string.status_error)
                    val err = intent.getStringExtra(AudioStreamService.EXTRA_CLIENT_IP) ?: "未知错误"
                    binding.clientInfo.text = "错误: $err"
                    binding.startButton.text = getString(R.string.start_server)
                    isStreaming = false
                }
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // 注册本地广播接收器
        LocalBroadcastManager.getInstance(this).registerReceiver(
            statusReceiver,
            IntentFilter(AudioStreamService.ACTION_STATUS)
        )

        // 显示本机 IP
        binding.ipAddress.text = getLocalIpAddress()

        binding.startButton.setOnClickListener {
            if (isStreaming) {
                stopService()
            } else {
                checkPermissionsAndStart()
            }
        }
    }

    override fun onDestroy() {
        LocalBroadcastManager.getInstance(this).unregisterReceiver(statusReceiver)
        super.onDestroy()
    }

    // ─── 权限检查 ────────────────────────────────────────────────

    private fun checkPermissionsAndStart() {
        val permissions = mutableListOf<String>()

        permissions.add(android.Manifest.permission.RECORD_AUDIO)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            permissions.add(android.Manifest.permission.POST_NOTIFICATIONS)
        }

        val needed = permissions.filter {
            checkSelfPermission(it) != android.content.pm.PackageManager.PERMISSION_GRANTED
        }

        if (needed.isNotEmpty()) {
            requestPermissions(needed.toTypedArray(), PERMISSION_REQUEST_CODE)
        } else {
            startService()
        }
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == PERMISSION_REQUEST_CODE) {
            val denied = grantResults.any { it != android.content.pm.PackageManager.PERMISSION_GRANTED }
            if (denied) {
                Toast.makeText(this, "需要录音和通知权限才能启动服务", Toast.LENGTH_LONG).show()
            } else {
                startService()
            }
        }
    }

    // ─── 启动/停止服务 ───────────────────────────────────────────

    private fun startService() {
        // 检查 WiFi 连接
        val wifiManager = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        if (wifiManager.isWifiEnabled) {
            val info = wifiManager.connectionInfo
            if (info != null && info.ssid != null && info.ssid != "<unknown ssid>") {
                // WiFi 已连接
            }
        }

        val intent = Intent(this, AudioStreamService::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(intent)
        } else {
            startService(intent)
        }
        isStreaming = true
    }

    private fun stopService() {
        val intent = Intent(this, AudioStreamService::class.java).apply {
            action = AudioStreamService.ACTION_STOP
        }
        startService(intent)
        isStreaming = false
    }

    // ─── 获取本机 IP ─────────────────────────────────────────────

    private fun getLocalIpAddress(): String {
        try {
            val interfaces = NetworkInterface.getNetworkInterfaces()
            while (interfaces.hasMoreElements()) {
                val networkInterface = interfaces.nextElement()
                // 跳过回环和未启用的接口
                if (networkInterface.isLoopback || !networkInterface.isUp) continue

                val addresses = networkInterface.inetAddresses
                while (addresses.hasMoreElements()) {
                    val address = addresses.nextElement()
                    if (address is Inet4Address && !address.isLoopbackAddress) {
                        return address.hostAddress ?: "未知"
                    }
                }
            }
        } catch (_: Exception) {}
        return "无法获取 IP (请连接 WiFi)"
    }

    companion object {
        private const val PERMISSION_REQUEST_CODE = 100
    }
}
