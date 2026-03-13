// SPDX-License-Identifier: MIT
// Copyright (c) 2023 Manuel Stoiber, German Aerospace Center (DLR)

#include <m3t/renderer_geometry.h>
#include <android/log.h>

#define LOG_TAG "M3T_NATIVE"
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)

namespace m3t {

RendererGeometry::RendererGeometry(const std::string &name) : name_{name} {}

RendererGeometry::~RendererGeometry() {
  if (initial_set_up_) {
    MakeContextCurrent();
    for (auto &render_data_body : render_data_bodies_) {
      DeleteGLVertexObjects(&render_data_body);
    }
    
    // Clean up EGL
    if (egl_display != EGL_NO_DISPLAY) {
        eglMakeCurrent(egl_display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
        eglDestroyContext(egl_display, egl_context);
        eglDestroySurface(egl_display, egl_surface);
        eglTerminate(egl_display);
    }
  }
}

bool RendererGeometry::SetUp() {
  const std::lock_guard<std::mutex> lock{mutex_};
  set_up_ = false;

  // initialize Headless EGL Context
  if (!initial_set_up_) {
    egl_display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
    eglInitialize(egl_display, nullptr, nullptr);

    EGLint config_attribs[] = {
        EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT,
        EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
        EGL_BLUE_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_RED_SIZE, 8,
        EGL_DEPTH_SIZE, 24,
        EGL_NONE
    };

    EGLConfig config;
    EGLint num_configs;
    eglChooseConfig(egl_display, config_attribs, &config, 1, &num_configs);

    EGLint context_attribs[] = { EGL_CONTEXT_CLIENT_VERSION, 3, EGL_NONE };
    egl_context = eglCreateContext(egl_display, config, EGL_NO_CONTEXT, context_attribs);

    EGLint pbuffer_attribs[] = { EGL_WIDTH, 1, EGL_HEIGHT, 1, EGL_NONE };
    egl_surface = eglCreatePbufferSurface(egl_display, config, pbuffer_attribs);

    initial_set_up_ = true;
  }

  if (!MakeContextCurrent()) return false;

  for (auto &body_ptr : body_ptrs_) {
    if (!body_ptr->set_up()) {
      std::cerr << "Body " << body_ptr->name() << " was not set up"
                << std::endl;
      return false;
    }
  }

  // Set up bodies
  for (auto &render_data_body : render_data_bodies_) {
    std::vector<float> vertex_data;
    AssembleVertexData(*render_data_body.body_ptr, &vertex_data);
    render_data_body.n_vertices = unsigned(vertex_data.size()) / 6;

    // Create GL Vertex objects
    if (set_up_) DeleteGLVertexObjects(&render_data_body);
    CreateGLVertexObjects(vertex_data, &render_data_body);
  }

  set_up_ = true;
  DetachContext(); 
  return true;
}

bool RendererGeometry::AddBody(const std::shared_ptr<Body> &body_ptr) {
  const std::lock_guard<std::mutex> lock{mutex_};

  // Check if renderer geometry for body already exists
  for (auto &p : body_ptrs_) {
    if (body_ptr->name() == p->name()) {
      std::cerr << "Body data " << body_ptr->name() << " already exists"
                << std::endl;
      return false;
    }
  }

  // Create data for body and assign parameters
  RenderDataBody render_data_body;
  render_data_body.body_ptr = body_ptr.get();
  if (set_up_ && body_ptr->set_up()) {
    // Assemble vertex data
    std::vector<float> vertex_data;
    AssembleVertexData(*body_ptr.get(), &vertex_data);
    render_data_body.n_vertices = unsigned(vertex_data.size()) / 6;

    // Create GL Vertex objects
    CreateGLVertexObjects(vertex_data, &render_data_body);
  } else if (set_up_ && !body_ptr->set_up()) {
    set_up_ = false;
  }

  // Add body ptr and body data
  body_ptrs_.push_back(body_ptr);
  render_data_bodies_.push_back(std::move(render_data_body));
  return true;
}

bool RendererGeometry::DeleteBody(const std::string &name) {
  const std::lock_guard<std::mutex> lock{mutex_};
  for (size_t i = 0; i < body_ptrs_.size(); ++i) {
    if (name == body_ptrs_[i]->name()) {
      body_ptrs_.erase(begin(body_ptrs_) + i);
      if (set_up_) {
        DeleteGLVertexObjects(&render_data_bodies_[i]);
      }
      render_data_bodies_.erase(begin(render_data_bodies_) + i);
      return true;
    }
  }
  std::cerr << "Body data \"" << name << "\" not found" << std::endl;
  return false;
}

void RendererGeometry::ClearBodies() {
  const std::lock_guard<std::mutex> lock{mutex_};
  if (set_up_) {
    for (auto &render_data_body : render_data_bodies_) {
      DeleteGLVertexObjects(&render_data_body);
    }
  }
  render_data_bodies_.clear();
  body_ptrs_.clear();
}

bool RendererGeometry::MakeContextCurrent() {
  if (eglMakeCurrent(egl_display, egl_surface, egl_surface, egl_context)) {
      return true;
  }
  LOGE("Failed to make EGL context current");
  return false;
}

bool RendererGeometry::DetachContext() {
  if (eglMakeCurrent(egl_display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT)) {
      return true;
  }
  return false;
}

const std::string &RendererGeometry::name() const { return name_; }

const std::vector<std::shared_ptr<Body>> &RendererGeometry::body_ptrs() const {
  return body_ptrs_;
}

const std::vector<RendererGeometry::RenderDataBody>
    &RendererGeometry::render_data_bodies() const {
  return render_data_bodies_;
}

bool RendererGeometry::set_up() const { return set_up_; }

void RendererGeometry::AssembleVertexData(const Body &body,
                                          std::vector<float> *vertex_data) {
  for (const auto &triangle_indices : body.mesh_indices()) {
    std::array<Eigen::Vector3f, 3> points;
    for (int i = 0; i < 3; ++i)
      points[i] = body.vertices()[triangle_indices[i]];

    Eigen::Vector3f normal{
        (points[2] - points[1]).cross(points[0] - points[1]).normalized()};

    for (auto point : points) {
      vertex_data->insert(end(*vertex_data), point.data(), point.data() + 3);
      vertex_data->insert(end(*vertex_data), normal.data(), normal.data() + 3);
    }
  }
}

void RendererGeometry::CreateGLVertexObjects(const std::vector<float> &vertices,
                                             RenderDataBody *render_data_body) {
  glGenVertexArrays(1, &render_data_body->vao);
  glBindVertexArray(render_data_body->vao);

  glGenBuffers(1, &render_data_body->vbo);
  glBindBuffer(GL_ARRAY_BUFFER, render_data_body->vbo);
  glBufferData(GL_ARRAY_BUFFER, vertices.size() * sizeof(float),
               &vertices.front(), GL_STATIC_DRAW);

  glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, 6 * sizeof(float), nullptr);
  glEnableVertexAttribArray(0);
  glVertexAttribPointer(1, 3, GL_FLOAT, GL_FALSE, 6 * sizeof(float),
                        (void *)(3 * sizeof(float)));
  glEnableVertexAttribArray(1);

  glBindBuffer(GL_ARRAY_BUFFER, 0);
  glBindVertexArray(0);
}

void RendererGeometry::DeleteGLVertexObjects(RenderDataBody *render_data_body) {
  glDeleteBuffers(1, &render_data_body->vbo);
  glDeleteVertexArrays(1, &render_data_body->vao);
}

}  // namespace m3t
