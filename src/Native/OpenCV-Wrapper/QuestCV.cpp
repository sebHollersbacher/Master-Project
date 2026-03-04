#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/calib3d.hpp>
#include <vector>

extern "C"
{
    // p3D: Array of x,y,z (size = count * 3)
    // p2D: Array of x,y   (size = count * 2)
    // camMatrixData: Array of 9 floats (3x3 matrix)
    // outputArr: Array of 7 floats to store result (tx, ty, tz, rx, ry, rz, success_flag)
    void SolvePnP_Ransac_Entry(float *p3D, int count, float *p2D, float *camMatrixData, float *outputArr)
    {

        // Convert raw floats to OpenCV Vectors
        std::vector<cv::Point3f> objectPoints;
        std::vector<cv::Point2f> imagePoints;

        for (int i = 0; i < count; i++)
        {
            objectPoints.push_back(cv::Point3f(p3D[i * 3], p3D[i * 3 + 1], p3D[i * 3 + 2]));
            imagePoints.push_back(cv::Point2f(p2D[i * 2], p2D[i * 2 + 1]));
        }

        cv::Mat camMatrix = cv::Mat(3, 3, CV_32F, camMatrixData);
        cv::Mat distCoeffs = cv::Mat::zeros(4, 1, CV_32F);

        cv::Mat rvec, tvec;
        std::vector<int> inliers;

        // Solve PnP with RANSAC
        bool success = cv::solvePnPRansac(
            objectPoints,
            imagePoints,
            camMatrix,
            distCoeffs,
            rvec,
            tvec,
            false,
            100,
            8.0f,
            0.99,
            inliers,
            cv::SOLVEPNP_ITERATIVE);

        if (success)
        {
            outputArr[0] = (float)tvec.at<double>(0); // tx
            outputArr[1] = (float)tvec.at<double>(1); // ty
            outputArr[2] = (float)tvec.at<double>(2); // tz
            outputArr[3] = (float)rvec.at<double>(0); // rx
            outputArr[4] = (float)rvec.at<double>(1); // ry
            outputArr[5] = (float)rvec.at<double>(2); // rz
            outputArr[6] = 1.0f;                      // Success
        }
        else
        {
            outputArr[6] = 0.0f; // Failure
        }
    }

    // p2D: Array of x,y (size = count * 2)
    // outputArr: Array of 4 floats (vx, vy, x0, y0)
    void FitLine2D_Entry(float* p2D, int count, float* outputArr) {
        if (count < 2) return;

        // Convert raw floats to OpenCV 2D Points
        std::vector<cv::Point2f> points;
        points.reserve(count);
        for (int i = 0; i < count; i++) {
            points.push_back(cv::Point2f(p2D[i * 2], p2D[i * 2 + 1])); 
        }

        // Fit Line
        std::vector<float> lineParams; 
        cv::fitLine(points, lineParams, cv::DIST_L2, 0, 0.01, 0.01);

        // Output
        for (int i = 0; i < 4; i++) {
            outputArr[i] = lineParams[i];
        }
    }
}