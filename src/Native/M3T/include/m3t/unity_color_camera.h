#ifndef M3T_UNITY_CAMERA_H_
#define M3T_UNITY_CAMERA_H_

#include <m3t/camera.h>
#include <m3t/common.h>

namespace m3t {

class UnityColorCamera : public ColorCamera {
 public:
  UnityColorCamera(const std::string &name);
  
  bool SetUp() override;

  bool UpdateImage(bool synchronized) override;

  // Custom method for Unity to push pixels
  void FeedImage(const cv::Mat &image);

  void SetIntrinsics(const Intrinsics &intrinsics);
};

}  // namespace m3t

#endif  // M3T_UNITY_CAMERA_H_