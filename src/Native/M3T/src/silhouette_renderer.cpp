// SPDX-License-Identifier: MIT
// Copyright (c) 2023 Manuel Stoiber, German Aerospace Center (DLR)

#include "m3t/silhouette_renderer.h"

#include <GLES3/gl3.h>
#include <GLES3/gl3ext.h>

#include <android/log.h>

#define LOG_TAG "M3T_NATIVE"
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)

namespace m3t {

std::string SilhouetteRendererCore::vertex_shader_code_ =
    "#version 300 es\n"
    "layout(location = 0) in vec3 aPos;\n"
    "uniform mat4 Trans;\n"
    "void main()\n"
    "{\n"
    "  gl_Position = Trans * vec4(aPos, 1.0);\n"
    "}";

std::string SilhouetteRendererCore::fragment_shader_code_ =
    "#version 300 es\n"
    "precision highp float;\n"
    "uniform float SilhouetteID;\n"
    "out vec4 FragColor;\n"
    "void main()\n"
    "{\n"
    "  // 1. Red Channel: Silhouette ID (Normalized to 0-1 for 8-bit color)\n"
    "  float r = SilhouetteID;\n"
    "  \n"
    "  // 2. Green & Blue Channels: Pack 16-bit depth (gl_FragCoord.z)\n"
    "  float depth = gl_FragCoord.z;\n"
    "  highp float depth16 = floor(depth * 65535.0);\n"
    "  float g = floor(depth16 / 256.0) / 255.0; // High byte\n"
    "  float b = mod(depth16, 256.0) / 255.0;  // Low byte\n"
    "  \n"
    "  FragColor = vec4(r, g, b, 1.0);\n"
    "}";

SilhouetteRendererCore::~SilhouetteRendererCore() {
  if (initial_set_up_) DeleteBufferObjects();
}

bool SilhouetteRendererCore::SetUp(
    const std::shared_ptr<RendererGeometry> &renderer_geometry_ptr,
    int image_width, int image_height) {
  renderer_geometry_ptr_ = renderer_geometry_ptr;
  image_width_ = image_width;
  image_height_ = image_height;
  image_rendered_ = false;

  // Create shader program
  if (!initial_set_up_ &&
      !CreateShaderProgram(renderer_geometry_ptr_.get(),
                           vertex_shader_code_.c_str(),
                           fragment_shader_code_.c_str(), &shader_program_))
    return false;

  // Create buffer objects
  if (initial_set_up_) DeleteBufferObjects();
  CreateBufferObjects();
  initial_set_up_ = true;
  return true;
}

bool SilhouetteRendererCore::StartRendering(
    const Eigen::Matrix4f &projection_matrix,
    const Transform3fA &world2camera_pose, IDType id_type) {
  if (!initial_set_up_) return false;
  renderer_geometry_ptr_->MakeContextCurrent();
  glViewport(0, 0, image_width_, image_height_);

  glBindFramebuffer(GL_FRAMEBUFFER, fbo_);
  glClearColor(0.0f, 0.0f, 0.0f, 0.0f);
  glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
  glEnable(GL_DEPTH_TEST);
  glFrontFace(GL_CCW);
  glCullFace(GL_FRONT);

  glUseProgram(shader_program_);
  for (const auto &render_data_body :
       renderer_geometry_ptr_->render_data_bodies()) {
    Eigen::Matrix4f trans{
        projection_matrix *
        (world2camera_pose * render_data_body.body_ptr->geometry2world_pose())
            .matrix()};

    unsigned loc;
    loc = glGetUniformLocation(shader_program_, "Trans");
    glUniformMatrix4fv(loc, 1, GL_FALSE, trans.data());
    loc = glGetUniformLocation(shader_program_, "SilhouetteID");
    // Map silhouette id from uchar [0, 255] to float [0.0, 1.0]
    glUniform1f(loc,
                float(render_data_body.body_ptr->get_id(id_type)) / 255.0f);

    if (render_data_body.body_ptr->geometry_enable_culling())
      glEnable(GL_CULL_FACE);
    else
      glDisable(GL_CULL_FACE);

    glBindVertexArray(render_data_body.vao);
    glDrawArrays(GL_TRIANGLES, 0, render_data_body.n_vertices);
    glBindVertexArray(0);
  }
  glBindFramebuffer(GL_FRAMEBUFFER, 0);
  renderer_geometry_ptr_->DetachContext();

  image_rendered_ = true;
  silhouette_image_fetched_ = false;
  depth_image_fetched_ = false;
  return true;
}

bool SilhouetteRendererCore::FetchSilhouetteImage(cv::Mat *silhouette_image) {
  if (!initial_set_up_ || !image_rendered_) return false;
  if (silhouette_image_fetched_) return true;
  renderer_geometry_ptr_->MakeContextCurrent();
  glPixelStorei(GL_PACK_ALIGNMENT, (silhouette_image->step & 3) ? 1 : 4);
  glPixelStorei(GL_PACK_ROW_LENGTH,
                GLint(silhouette_image->step / silhouette_image->elemSize()));
  glBindFramebuffer(GL_FRAMEBUFFER, fbo_);
  glBindRenderbuffer(GL_RENDERBUFFER, rbo_silhouette_);
  glReadPixels(0, 0, image_width_, image_height_, GL_RED, GL_UNSIGNED_BYTE,
               silhouette_image->data);
  glBindRenderbuffer(GL_RENDERBUFFER, 0);
  glBindFramebuffer(GL_FRAMEBUFFER, 0);
  renderer_geometry_ptr_->DetachContext();
  silhouette_image_fetched_ = true;
  return true;
}

bool SilhouetteRendererCore::FetchDepthImage(cv::Mat *depth_image,
                                             cv::Mat *silhouette_image) {
  if (!initial_set_up_ || !image_rendered_) return false;
  if (depth_image_fetched_) return true;

  renderer_geometry_ptr_->MakeContextCurrent();

  // Create a temporary buffer for the RGBA data
  std::vector<uchar> rgba_buffer(image_width_ * image_height_ * 4);

  glBindFramebuffer(GL_FRAMEBUFFER, fbo_);

  // (This should be your existing glReadPixels call)
  glReadPixels(0, 0, image_width_, image_height_, GL_RGBA, GL_UNSIGNED_BYTE,
               rgba_buffer.data());

  // In FetchDepthImage, after glReadPixels:
  int centerX = image_width_ / 2;
  int centerY = image_height_ / 2;
  int pixelIdx = (centerY * image_width_ + centerX) * 4;

  LOGI("RGBA CENTER (%d,%d): R:%d G:%d B:%d", centerX, centerY,
       rgba_buffer[pixelIdx], rgba_buffer[pixelIdx + 1],
       rgba_buffer[pixelIdx + 2]);

  glBindFramebuffer(GL_FRAMEBUFFER, 0);
  renderer_geometry_ptr_->DetachContext();

  // 3. Unpack the data into both matrices
  ushort *depth_ptr = (ushort *)depth_image->data;
  uchar *sil_ptr = silhouette_image->data;

  for (int i = 0; i < image_width_ * image_height_; ++i) {
    int idx = i * 4;

    // Red Channel -> Silhouette ID
    sil_ptr[i] = rgba_buffer[idx];

    // Green & Blue -> 16-bit Depth
    uchar g = rgba_buffer[idx + 1];
    uchar b = rgba_buffer[idx + 2];
    depth_ptr[i] = (ushort(g) << 8) | ushort(b);
  }
  depth_image_fetched_ = true;
  silhouette_image_fetched_ = true;  // Mark both as fetched
  return true;
}

void SilhouetteRendererCore::CreateBufferObjects() {
  renderer_geometry_ptr_->MakeContextCurrent();

  // Color Renderbuffer - Use GL_RGBA8 specifically
  glGenRenderbuffers(1, &rbo_silhouette_);
  glBindRenderbuffer(GL_RENDERBUFFER, rbo_silhouette_);
  glRenderbufferStorage(GL_RENDERBUFFER, GL_RGBA8, image_width_, image_height_);

  // Depth Renderbuffer - Use GL_DEPTH_COMPONENT24 for better compatibility
  // with RGBA8
  glGenRenderbuffers(1, &rbo_depth_);
  glBindRenderbuffer(GL_RENDERBUFFER, rbo_depth_);
  glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH_COMPONENT24, image_width_,
                        image_height_);

  // Initialize framebuffer bodies_render_data
  glGenFramebuffers(1, &fbo_);
  glBindFramebuffer(GL_FRAMEBUFFER, fbo_);

  // Attach Color
  glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0,
                            GL_RENDERBUFFER, rbo_silhouette_);

  // Attach Depth
  glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT,
                            GL_RENDERBUFFER, rbo_depth_);

  // check for error
  GLenum status = glCheckFramebufferStatus(GL_FRAMEBUFFER);
  if (status == GL_FRAMEBUFFER_COMPLETE) {
    LOGI("FBO SUCCESS: Framebuffer is complete (0x%x)", status);
  } else {
    LOGE("FBO STILL INCOMPLETE: 0x%x", status);
  }

  GLint linkStatus;
  glGetProgramiv(shader_program_, GL_LINK_STATUS, &linkStatus);
  if (linkStatus != GL_TRUE) {
    char log[512];
    glGetProgramInfoLog(shader_program_, 512, NULL, log);
    LOGE("SHADER LINK ERROR: %s", log);
  }

  glBindFramebuffer(GL_FRAMEBUFFER, 0);
  renderer_geometry_ptr_->DetachContext();
}

void SilhouetteRendererCore::DeleteBufferObjects() {
  renderer_geometry_ptr_->MakeContextCurrent();
  glDeleteRenderbuffers(1, &rbo_silhouette_);
  glDeleteRenderbuffers(1, &rbo_depth_);
  glDeleteFramebuffers(1, &fbo_);
  renderer_geometry_ptr_->DetachContext();
}

FullSilhouetteRenderer::FullSilhouetteRenderer(
    const std::string &name,
    const std::shared_ptr<RendererGeometry> &renderer_geometry_ptr,
    const Transform3fA &world2camera_pose, const Intrinsics &intrinsics,
    IDType id_type, float z_min, float z_max)
    : FullDepthRenderer{name,
                        renderer_geometry_ptr,
                        world2camera_pose,
                        intrinsics,
                        z_min,
                        z_max},
      id_type_{id_type} {}

FullSilhouetteRenderer::FullSilhouetteRenderer(
    const std::string &name,
    const std::shared_ptr<RendererGeometry> &renderer_geometry_ptr,
    const std::shared_ptr<Camera> &camera_ptr, IDType id_type, float z_min,
    float z_max)
    : FullDepthRenderer{name, renderer_geometry_ptr, camera_ptr, z_min, z_max},
      id_type_{id_type} {}

FullSilhouetteRenderer::FullSilhouetteRenderer(
    const std::string &name, const std::filesystem::path &metafile_path,
    const std::shared_ptr<RendererGeometry> &renderer_geometry_ptr,
    const std::shared_ptr<Camera> &camera_ptr)
    : FullDepthRenderer{name, metafile_path, renderer_geometry_ptr,
                        camera_ptr} {}

bool FullSilhouetteRenderer::SetUp() {
  const std::lock_guard<std::mutex> lock{mutex_};
  set_up_ = false;
  if (!metafile_path_.empty())
    if (!LoadMetaData()) return false;

  // Check if all required objects are set up
  if (!renderer_geometry_ptr_->set_up()) {
    std::cerr << "Renderer geometry " << renderer_geometry_ptr_->name()
              << " was not set up" << std::endl;
    return false;
  }
  if (camera_ptr_ && !InitParametersFromCamera()) return false;

  // Set up everything
  CalculateProjectionMatrix();
  CalculateProjectionTerms();
  ClearDepthImage();
  ClearSilhouetteImage();
  if (!core_.SetUp(renderer_geometry_ptr_, intrinsics_.width,
                   intrinsics_.height))
    return false;

  set_up_ = true;
  return true;
}

void FullSilhouetteRenderer::set_id_type(IDType id_type) { id_type_ = id_type; }

bool FullSilhouetteRenderer::StartRendering() {
  const std::lock_guard<std::mutex> lock{mutex_};
  if (!set_up_) {
    std::cerr << "Set up renderer " << name_ << " first" << std::endl;
    return false;
  }
  return core_.StartRendering(projection_matrix_, world2camera_pose_, id_type_);
}

bool FullSilhouetteRenderer::FetchSilhouetteImage() {
  const std::lock_guard<std::mutex> lock{mutex_};
  if (!set_up_) {
    std::cerr << "Set up renderer " << name_ << " first" << std::endl;
    return false;
  }
  return core_.FetchSilhouetteImage(&silhouette_image_);
}

bool FullSilhouetteRenderer::FetchDepthImage() {
  const std::lock_guard<std::mutex> lock{mutex_};
  if (!set_up_) {
    std::cerr << "Set up renderer " << name_ << " first" << std::endl;
    return false;
  }
  return core_.FetchDepthImage(&depth_image_, &silhouette_image_);
}

IDType FullSilhouetteRenderer::id_type() const { return id_type_; }

const cv::Mat &FullSilhouetteRenderer::silhouette_image() const {
  return silhouette_image_;
}

uchar FullSilhouetteRenderer::SilhouetteValue(
    const cv::Point2i &image_coordinate) const {
  return silhouette_image_.at<uchar>(image_coordinate);
}

bool FullSilhouetteRenderer::LoadMetaData() {
  // Open file storage from yaml
  cv::FileStorage fs;
  if (!OpenYamlFileStorage(metafile_path_, &fs)) return false;

  // Read parameters from yaml
  ReadOptionalValueFromYaml(fs, "z_min", &z_min_);
  ReadOptionalValueFromYaml(fs, "z_max", &z_max_);
  ReadOptionalValueFromYaml(fs, "id_type", &id_type_);
  fs.release();
  return true;
}

void FullSilhouetteRenderer::ClearSilhouetteImage() {
  silhouette_image_.create(cv::Size{intrinsics_.width, intrinsics_.height},
                           CV_8U);
  silhouette_image_.setTo(cv::Scalar{0});
}

FocusedSilhouetteRenderer::FocusedSilhouetteRenderer(
    const std::string &name,
    const std::shared_ptr<RendererGeometry> &renderer_geometry_ptr,
    const Transform3fA &world2camera_pose, const Intrinsics &intrinsics,
    IDType id_type, int image_size, float z_min, float z_max)
    : FocusedDepthRenderer{name,
                           renderer_geometry_ptr,
                           world2camera_pose,
                           intrinsics,
                           image_size,
                           z_min,
                           z_max},
      id_type_{id_type} {}

FocusedSilhouetteRenderer::FocusedSilhouetteRenderer(
    const std::string &name,
    const std::shared_ptr<RendererGeometry> &renderer_geometry_ptr,
    const std::shared_ptr<Camera> &camera_ptr, IDType id_type, int image_size,
    float z_min, float z_max)
    : FocusedDepthRenderer{name,       renderer_geometry_ptr,
                           camera_ptr, image_size,
                           z_min,      z_max},
      id_type_{id_type} {}

FocusedSilhouetteRenderer::FocusedSilhouetteRenderer(
    const std::string &name, const std::filesystem::path &metafile_path,
    const std::shared_ptr<RendererGeometry> &renderer_geometry_ptr,
    const std::shared_ptr<Camera> &camera_ptr)
    : FocusedDepthRenderer{name, metafile_path, renderer_geometry_ptr,
                           camera_ptr} {}

bool FocusedSilhouetteRenderer::SetUp() {
  const std::lock_guard<std::mutex> lock{mutex_};
  set_up_ = false;
  if (!metafile_path_.empty())
    if (!LoadMetaData()) return false;

  // Check if all required objects are set up
  if (!renderer_geometry_ptr_->set_up()) {
    std::cerr << "Renderer geometry " << renderer_geometry_ptr_->name()
              << " was not set up" << std::endl;
    return false;
  }
  if (camera_ptr_ && !InitParametersFromCamera()) return false;
  if (referenced_body_ptrs_.empty()) {
    std::cerr << "No referenced bodies were assigned to renderer " << name_
              << std::endl;
    return false;
  }
  for (auto &referenced_body_ptr : referenced_body_ptrs_) {
    if (!referenced_body_ptr->set_up()) {
      std::cerr << "Body " << referenced_body_ptr->name() << " was not set up"
                << std::endl;
      return false;
    }
  }

  // Set up everything
  CalculateProjectionMatrix();
  CalculateProjectionTerms();
  ClearDepthImage();
  ClearSilhouetteImage();
  if (!core_.SetUp(renderer_geometry_ptr_, image_size_, image_size_))
    return false;

  set_up_ = true;
  return true;
}

void FocusedSilhouetteRenderer::set_id_type(IDType id_type) {
  id_type_ = id_type;
}

bool FocusedSilhouetteRenderer::StartRendering() {
  std::lock_guard<std::mutex> lock(mutex_);
  if (!set_up_) {
    std::cerr << "Set up renderer " << name_ << " first" << std::endl;
    return false;
  }
  CalculateProjectionMatrix();
  return core_.StartRendering(projection_matrix_, world2camera_pose_, id_type_);
}

bool FocusedSilhouetteRenderer::FetchSilhouetteImage() {
  const std::lock_guard<std::mutex> lock{mutex_};
  if (!set_up_) {
    std::cerr << "Set up renderer " << name_ << " first" << std::endl;
    return false;
  }
  return core_.FetchSilhouetteImage(&focused_silhouette_image_);
}

bool FocusedSilhouetteRenderer::FetchDepthImage() {
  const std::lock_guard<std::mutex> lock{mutex_};
  if (!set_up_) {
    std::cerr << "Set up renderer " << name_ << " first" << std::endl;
    return false;
  }
  return core_.FetchDepthImage(&focused_depth_image_,
                               &focused_silhouette_image_);
}

IDType FocusedSilhouetteRenderer::id_type() const { return id_type_; }

const cv::Mat &FocusedSilhouetteRenderer::focused_silhouette_image() const {
  return focused_silhouette_image_;
}

uchar FocusedSilhouetteRenderer::SilhouetteValue(
    const cv::Point2i &image_coordinate) const {
  int u = int((image_coordinate.x - corner_u_) * scale_ + 0.5f);
  int v = int((image_coordinate.y - corner_v_) * scale_ + 0.5f);
  return focused_silhouette_image_.at<uchar>(v, u);
}

bool FocusedSilhouetteRenderer::LoadMetaData() {
  // Open file storage from yaml
  cv::FileStorage fs;
  if (!OpenYamlFileStorage(metafile_path_, &fs)) return false;

  // Read parameters from yaml
  ReadOptionalValueFromYaml(fs, "id_type", &id_type_);
  ReadOptionalValueFromYaml(fs, "image_size", &image_size_);
  ReadOptionalValueFromYaml(fs, "z_min", &z_min_);
  ReadOptionalValueFromYaml(fs, "z_max", &z_max_);
  fs.release();
  return true;
}

void FocusedSilhouetteRenderer::ClearSilhouetteImage() {
  focused_silhouette_image_.create(cv::Size{image_size_, image_size_}, CV_8U);
  focused_silhouette_image_.setTo(cv::Scalar{0});
}

}  // namespace m3t
