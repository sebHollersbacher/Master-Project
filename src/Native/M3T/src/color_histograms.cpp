// SPDX-License-Identifier: MIT
// Copyright (c) 2023 Manuel Stoiber, German Aerospace Center (DLR)

#include <m3t/color_histograms.h>

namespace m3t {

ColorHistograms::ColorHistograms(const std::string& name, int n_bins,
                                 float learning_rate_f, float learning_rate_b)
    : name_{name},
      n_bins_{n_bins},
      learning_rate_f_{learning_rate_f},
      learning_rate_b_{learning_rate_b} {}

ColorHistograms::ColorHistograms(const std::string& name,
                                 const std::filesystem::path& metafile_path)
    : name_{name}, metafile_path_{metafile_path} {}

bool ColorHistograms::SetUp() {
  set_up_ = false;
  if (!metafile_path_.empty())
    if (!LoadMetaData()) return false;
  if (!PrecalculateVariables()) return false;
  SetUpHistograms();
  set_up_ = true;

  if (has_foreground_texture_) BuildTextureHistogramCache();

  return true;
}

void ColorHistograms::set_name(const std::string& name) { name_ = name; }

void ColorHistograms::set_metafile_path(
    const std::filesystem::path& metafile_path) {
  metafile_path_ = metafile_path;
  set_up_ = false;
}

void ColorHistograms::set_n_bins(int n_bins) {
  n_bins_ = n_bins;
  set_up_ = false;
}

void ColorHistograms::set_learning_rate_f(float learning_rate_f) {
  learning_rate_f_ = learning_rate_f;
}

void ColorHistograms::set_learning_rate_b(float learning_rate_b) {
  learning_rate_b_ = learning_rate_b;
}

bool ColorHistograms::ClearMemory() {
  if (!set_up_) {
    std::cerr << "Set up color histogram " << name_ << " first" << std::endl;
    return false;
  }
  std::fill(begin(histogram_memory_f_), end(histogram_memory_f_), 0.0f);
  std::fill(begin(histogram_memory_b_), end(histogram_memory_b_), 0.0f);
  return true;
}

void ColorHistograms::AddForegroundColor(const cv::Vec3b& pixel_color) {
  histogram_memory_f_[int((pixel_color[0] >> bitshift_) * n_bins_squared_) +
                      int((pixel_color[1] >> bitshift_) * n_bins_) +
                      int(pixel_color[2] >> bitshift_)] += 1.0f;
}

void ColorHistograms::AddBackgroundColor(const cv::Vec3b& pixel_color) {
  histogram_memory_b_[int((pixel_color[0] >> bitshift_) * n_bins_squared_) +
                      int((pixel_color[1] >> bitshift_) * n_bins_) +
                      int(pixel_color[2] >> bitshift_)] += 1.0f;
}

bool ColorHistograms::InitializeHistograms() {
  if (!set_up_) {
    std::cerr << "Set up color histogram " << name_ << " first" << std::endl;
    return false;
  }

  if (!BuildHistogramFromTexture(&histogram_f_)) {
    CalculateHistogram(1.0f, histogram_memory_f_, &histogram_f_);
  }

  CalculateHistogram(1.0f, histogram_memory_b_, &histogram_b_);
  ClearMemory();
  return true;
}

bool ColorHistograms::UpdateHistograms() {
  if (!set_up_) {
    std::cerr << "Set up color histogram " << name_ << " first" << std::endl;
    return false;
  }

  float effective_lr_f = learning_rate_f_;
  if (boost_frames_remaining_ > 0) {
    effective_lr_f = boosted_learning_rate_f_;
    boost_frames_remaining_--;

    if (boost_frames_remaining_ == 0 && has_foreground_texture_) {
      SnapshotForegroundReference();
    }
}

  CalculateHistogram(effective_lr_f, histogram_memory_f_, &histogram_f_);
  CalculateHistogram(learning_rate_b_, histogram_memory_b_, &histogram_b_);
  ClearMemory();
  return true;
}

void ColorHistograms::StartLearningBoost(int n_frames, float boost_rate) {
  boost_frames_remaining_ = n_frames;
  boosted_learning_rate_f_ = boost_rate;
}

void ColorHistograms::SetForegroundTexture(const cv::Mat& texture) {
  if (texture.empty()) return;

  if (texture.channels() == 4) {
    cv::Mat channels[4];
    cv::split(texture, channels);
    cv::merge(std::vector<cv::Mat>{channels[0], channels[1], channels[2]},
              foreground_texture_);
  } else {
    foreground_texture_ = texture.clone();
  }
  has_foreground_texture_ = true;

  if (set_up_) BuildTextureHistogramCache();
}

void ColorHistograms::BuildTextureHistogramCache() {
  texture_hist_normalized_.assign(n_bins_cubed_, 0.0f);
  float sum = 0.0f;

  cv::Mat gray;
  cv::cvtColor(foreground_texture_, gray, cv::COLOR_BGR2GRAY);

  for (int r = 0; r < foreground_texture_.rows; ++r) {
    const auto* pix = foreground_texture_.ptr<cv::Vec3b>(r);
    const auto* g = gray.ptr<uchar>(r);
    for (int c = 0; c < foreground_texture_.cols; ++c) {
      if (g[c] <= 5) continue;
      int idx = int((pix[c][0] >> bitshift_) * n_bins_squared_) +
                int((pix[c][1] >> bitshift_) * n_bins_) +
                int(pix[c][2] >> bitshift_);
      texture_hist_normalized_[idx] += 1.0f;
      sum += 1.0f;
    }
  }

  if (sum > 0.0f) {
    for (int i = 0; i < n_bins_cubed_; ++i) texture_hist_normalized_[i] /= sum;
  }
}

void ColorHistograms::SnapshotForegroundReference() {
  if (!set_up_) return;
  snapshot_hist_ = histogram_f_;
  has_snapshot_ = true;
}

bool ColorHistograms::BuildHistogramFromTexture(std::vector<float>* histogram) {
  if (!has_foreground_texture_ || !set_up_) return false;

  std::fill(begin(histogram_memory_f_), end(histogram_memory_f_), 0.0f);

  cv::Mat mask, gray;
  cv::cvtColor(foreground_texture_, gray, cv::COLOR_BGR2GRAY);
  cv::threshold(gray, mask, 5, 255, cv::THRESH_BINARY);

  int count = 0;
  for (int r = 0; r < foreground_texture_.rows; ++r) {
    const auto* pix = foreground_texture_.ptr<cv::Vec3b>(r);
    const auto* m = mask.ptr<uchar>(r);
    for (int c = 0; c < foreground_texture_.cols; ++c) {
      if (m[c] == 0) continue;
      AddForegroundColor(pix[c]);
      ++count;
    }
  }

  if (count == 0) return false;

  CalculateHistogram(1.0f, histogram_memory_f_, histogram);
  std::fill(begin(histogram_memory_f_), end(histogram_memory_f_), 0.0f);
  return true;
}

bool ColorHistograms::ResetForegroundFromTexture() {
  has_snapshot_ = false;
  return BuildHistogramFromTexture(&histogram_f_);
}

float ColorHistograms::ForegroundDivergence() const {
  // Prefer snapshot (actual camera colors), fall back to texture
  const std::vector<float>* reference = nullptr;
  if (has_snapshot_)
    reference = &snapshot_hist_;
  else if (has_foreground_texture_ && !texture_hist_normalized_.empty())
    reference = &texture_hist_normalized_;
  else
    return -1.0f;

  const int chroma_bins = 8;
  const int chroma_total = chroma_bins * chroma_bins;
  const float scale = 256.0f / float(n_bins_);

  std::vector<float> chroma_f(chroma_total, 0.0f);
  std::vector<float> chroma_r(chroma_total, 0.0f);

  for (int d0 = 0; d0 < n_bins_; ++d0) {
    float v0 = (d0 + 0.5f) * scale;
    for (int d1 = 0; d1 < n_bins_; ++d1) {
      float v1 = (d1 + 0.5f) * scale;
      for (int d2 = 0; d2 < n_bins_; ++d2) {
        float v2 = (d2 + 0.5f) * scale;
        float total = v0 + v1 + v2;

        if (total < 30.0f) continue;

        int fine_idx = d0 * n_bins_squared_ + d1 * n_bins_ + d2;

        float c0 = v0 / total;
        float c1 = v1 / total;
        int cb0 = std::min(int(c0 * chroma_bins), chroma_bins - 1);
        int cb1 = std::min(int(c1 * chroma_bins), chroma_bins - 1);
        int ci = cb0 * chroma_bins + cb1;

        chroma_f[ci] += histogram_f_[fine_idx];
        chroma_r[ci] += (*reference)[fine_idx];
      }
    }
  }

  float bc = 0.0f;
  for (int i = 0; i < chroma_total; ++i)
    bc += std::sqrt(chroma_f[i] * chroma_r[i]);

  return std::sqrt(std::max(0.0f, 1.0f - bc));
}

void ColorHistograms::GetProbabilities(const cv::Vec3b& pixel_color,
                                       float* pixel_color_probability_f,
                                       float* pixel_color_probability_b) const {
  int idx = (pixel_color[0] >> bitshift_) * n_bins_squared_ +
            (pixel_color[1] >> bitshift_) * n_bins_ +
            (pixel_color[2] >> bitshift_);
  *pixel_color_probability_f = histogram_f_[idx];
  *pixel_color_probability_b = histogram_b_[idx];
}

const std::string& ColorHistograms::name() const { return name_; }

const std::filesystem::path& ColorHistograms::metafile_path() const {
  return metafile_path_;
}

int ColorHistograms::n_bins() const { return n_bins_; }

float ColorHistograms::learning_rate_f() const { return learning_rate_f_; }

float ColorHistograms::learning_rate_b() const { return learning_rate_b_; }

bool ColorHistograms::set_up() const { return set_up_; }

bool ColorHistograms::LoadMetaData() {
  // Open file storage from yaml
  cv::FileStorage fs;
  if (!OpenYamlFileStorage(metafile_path_, &fs)) return false;

  // Read parameters from yaml
  ReadOptionalValueFromYaml(fs, "n_bins", &n_bins_);
  ReadOptionalValueFromYaml(fs, "learning_rate_f", &learning_rate_f_);
  ReadOptionalValueFromYaml(fs, "learning_rate_b", &learning_rate_b_);
  fs.release();
  return true;
}

bool ColorHistograms::PrecalculateVariables() {
  switch (n_bins_) {
    case 2:
      bitshift_ = 7;
      break;
    case 4:
      bitshift_ = 6;
      break;
    case 8:
      bitshift_ = 5;
      break;
    case 16:
      bitshift_ = 4;
      break;
    case 32:
      bitshift_ = 3;
      break;
    case 64:
      bitshift_ = 2;
      break;
    default:
      std::cerr << "n_bins = " << n_bins_ << " in histogram " << name_
                << " not valid."
                << "Has to be of value 2, 4, 8, 16, 32, or 64" << std::endl;
      return false;
  }
  n_bins_squared_ = pow_int(n_bins_, 2);
  n_bins_cubed_ = pow_int(n_bins_, 3);
  return true;
}

void ColorHistograms::SetUpHistograms() {
  histogram_memory_f_.resize(n_bins_cubed_);
  histogram_memory_b_.resize(n_bins_cubed_);
  histogram_f_.resize(n_bins_cubed_);
  histogram_b_.resize(n_bins_cubed_);
  std::fill(begin(histogram_memory_f_), end(histogram_memory_f_), 0.0f);
  std::fill(begin(histogram_memory_b_), end(histogram_memory_b_), 0.0f);
  float uniform_value = 1.0f / float(n_bins_cubed_);
  std::fill(begin(histogram_f_), end(histogram_f_), uniform_value);
  std::fill(begin(histogram_b_), end(histogram_b_), uniform_value);
}

void ColorHistograms::CalculateHistogram(
    float learning_rate, const std::vector<float>& histogram_memory,
    std::vector<float>* histogram) {
  // Calculate sum for normalization
  float sum = 0.0f;
#ifndef _DEBUG
#pragma omp simd
#endif
  for (int i = 0; i < n_bins_cubed_; i++) {
    sum += histogram_memory[i];
  }

  // Apply uniform value if sum is zero and learning rate 1
  if (!sum) {
    if (learning_rate == 1.0f) {
      float uniform_value = 1.0f / n_bins_cubed_;
      std::fill(begin(*histogram), end(*histogram), uniform_value);
    }
    return;
  }

  // Calculate histogram
  float complement_learning_rate = 1.0f - learning_rate;
  float learning_rate_divide_sum = learning_rate / sum;
  if (complement_learning_rate == 0.0f) {
#ifndef _DEBUG
#pragma omp simd
#endif
    for (int i = 0; i < n_bins_cubed_; i++) {
      (*histogram)[i] = histogram_memory[i] * learning_rate_divide_sum;
    }
  } else {
#ifndef _DEBUG
#pragma omp simd
#endif
    for (int i = 0; i < n_bins_cubed_; i++) {
      (*histogram)[i] *= complement_learning_rate;
      (*histogram)[i] += histogram_memory[i] * learning_rate_divide_sum;
    }
  }
}

}  // namespace m3t