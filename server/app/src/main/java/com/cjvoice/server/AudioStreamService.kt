package com.cjvoice.server

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Build
import android.os.IBinder
import android.os.Process
import android.util.Log
import java.io.IOException
import java.io.OutputStream
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.atomic.AtomicBoolean

/**
 * 前台服务：采集麦克风音频并通过 WiFi TCP Socket 流式传输
 *
 * 音频格式: 16kHz, 16-bit, 单声道 PCM
 * Socket: 监听端口 12345, 接受第一个连接的客户端
 */
class AudioStreamService : Service() {

    companion object {
        private const val TAG = "AudioStreamService"
        const val PORT = 12345
        const val SAMPLE_RATE = 16000

        /** 广播 Action 用于更新 UI */
        const val ACTION_STATUS = "com.cjvoice.server.STATUS"
        const val EXTRA_STATUS = "status"
        const val EXTRA_CLIENT_IP = "client_ip"

        const val STATUS_LISTENING = "listening"
        const val STATUS_STREAMING = "streaming"
        const val STATUS_STOPPED = "stopped"
        const val STATUS_ERROR = "error"

        /** 控制广播 */
        const val ACTION_STOP = "com.cjvoice.server.STOP"

        private const val NOTIFICATION_ID = 1001
        private const val CHANNEL_ID = "audio_stream_channel"
    }

    private var serverSocket: ServerSocket? = null
    private var clientSocket: Socket? = null
    private var audioRecord: AudioRecord? = null
    private var acceptThread: Thread? = null
    private var streamThread: Thread? = null

    private val isRunning = AtomicBoolean(false)

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                stopStreaming()
                stopSelf()
                return START_NOT_STICKY
            }
            else -> {
                // 启动前台服务
                val notification = buildNotification(getString(R.string.notification_text_streaming))
                startForeground(NOTIFICATION_ID, notification)

                // 启动 Socket 服务
                startServer()
            }
        }
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        stopStreaming()
        super.onDestroy()
    }

    // ─── 通知通道 ────────────────────────────────────────────────

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            getString(R.string.channel_name_audio_service),
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = getString(R.string.channel_desc_audio_service)
        }
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(channel)
    }

    private fun buildNotification(text: String): Notification {
        val builder = Notification.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.app_name))
            .setContentText(text)
            .setSmallIcon(android.R.drawable.ic_menu_mic)
            .setOngoing(true)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            builder.setForegroundServiceBehavior(
                Notification.FOREGROUND_SERVICE_IMMEDIATE
            )
        }
        return builder.build()
    }

    // ─── Socket 服务 ─────────────────────────────────────────────

    private fun startServer() {
        if (isRunning.get()) return
        isRunning.set(true)

        acceptThread = Thread({ runAcceptLoop() }, "accept-thread").apply {
            // 设置音频优先级
            priority = Thread.MAX_PRIORITY
        }
        acceptThread?.start()
    }

    private fun runAcceptLoop() {
        try {
            serverSocket = ServerSocket(PORT)
            Log.i(TAG, "Server socket listening on port $PORT")
            broadcastStatus(STATUS_LISTENING)

            // 接受第一个客户端连接
            val socket = serverSocket!!.accept()
            clientSocket = socket
            val clientIp = socket.inetAddress.hostAddress ?: "unknown"
            Log.i(TAG, "Client connected: $clientIp")
            broadcastStatus(STATUS_STREAMING, clientIp)

            // 开始音频采集与传输
            startAudioStream(socket)

        } catch (e: IOException) {
            Log.e(TAG, "Server error: ${e.message}")
            broadcastStatus(STATUS_ERROR, e.message ?: "Server error")
        }
    }

    // ─── 音频采集与传输 ──────────────────────────────────────────

    private fun startAudioStream(socket: Socket) {
        streamThread = Thread({ runStreamLoop(socket) }, "audio-stream-thread").apply {
            // 音频线程最高优先级
            priority = Thread.MAX_PRIORITY
        }
        streamThread?.start()
    }

    private fun runStreamLoop(socket: Socket) {
        Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO)

        val outputStream: OutputStream = socket.getOutputStream()
        val bufferSize = AudioRecord.getMinBufferSize(
            SAMPLE_RATE,
            AudioFormat.CHANNEL_IN_MONO,
            AudioFormat.ENCODING_PCM_16BIT
        )

        audioRecord = AudioRecord(
            MediaRecorder.AudioSource.VOICE_COMMUNICATION,
            SAMPLE_RATE,
            AudioFormat.CHANNEL_IN_MONO,
            AudioFormat.ENCODING_PCM_16BIT,
            bufferSize * 2  // 双倍缓冲区减少丢帧
        )

        if (audioRecord?.state != AudioRecord.STATE_INITIALIZED) {
            Log.e(TAG, "AudioRecord init failed")
            broadcastStatus(STATUS_ERROR, "AudioRecord init failed")
            return
        }

        val buffer = ByteArray(bufferSize)
        audioRecord?.startRecording()

        Log.i(TAG, "Audio streaming started")

        try {
            while (isRunning.get() && !socket.isClosed) {
                val bytesRead = audioRecord?.read(buffer, 0, buffer.size) ?: -1
                if (bytesRead > 0) {
                    outputStream.write(buffer, 0, bytesRead)
                    outputStream.flush()
                }
            }
        } catch (e: IOException) {
            Log.e(TAG, "Stream error: ${e.message}")
            broadcastStatus(STATUS_ERROR, e.message ?: "Stream error")
        } finally {
            cleanup()
        }
    }

    // ─── 清理 ────────────────────────────────────────────────────

    private fun stopStreaming() {
        isRunning.set(false)
        cleanup()
    }

    private fun cleanup() {
        try {
            audioRecord?.apply {
                if (recordingState == AudioRecord.RECORDSTATE_RECORDING) {
                    stop()
                }
                release()
            }
        } catch (_: Exception) {}
        audioRecord = null

        try { clientSocket?.close() } catch (_: Exception) {}
        clientSocket = null

        try { serverSocket?.close() } catch (_: Exception) {}
        serverSocket = null

        acceptThread = null
        streamThread = null

        broadcastStatus(STATUS_STOPPED)
    }

    // ─── 状态广播 ────────────────────────────────────────────────

    private fun broadcastStatus(status: String, extra: String = "") {
        val intent = Intent(ACTION_STATUS).apply {
            putExtra(EXTRA_STATUS, status)
            if (extra.isNotEmpty()) {
                putExtra(EXTRA_CLIENT_IP, extra)
            }
        }
        sendBroadcast(intent)
    }
}
