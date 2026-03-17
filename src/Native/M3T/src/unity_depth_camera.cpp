#include <m3t/unity_depth_camera.h>
#include <android/log.h>

#define LOG_TAG "M3T_NATIVE"
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)

namespace m3t {

UnityDepthCamera::UnityDepthCamera(const std::string &name) : DepthCamera{name} {
  set_up_ = false;
}

bool UnityDepthCamera::SetUp() {
  set_up_ = true;
  return true;
}

bool UnityDepthCamera::UpdateImage(bool synchronized) {
  // We don't pull data; we wait for FeedImage to be called by the wrapper
  return true;
}

void UnityDepthCamera::FeedImage(const cv::Mat &image) {
  image_ = image;
  set_up_ = true;
}

void UnityDepthCamera::SetIntrinsics(const Intrinsics &intrinsics) {
  intrinsics_ = intrinsics;
}

}  // namespace m3t