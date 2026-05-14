#include <opencv2/core.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/calib3d.hpp>
#include <vector>

extern "C"
{
    // p3D: Array of x,y,z (size = count * 3)
    // p2D: Array of x,y   (size = count * 2)
    // camMatrixData: Array of 9 floats (3x3 matrix)
    // outputArr: Array of 9 floats (tx, ty, tz, rx, ry, rz, success, inlierCount, meanReprojError)
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
            200,
            5.0f,
            0.99,
            inliers,
            cv::SOLVEPNP_EPNP);

        if (success && (int)inliers.size() >= 4)
        {
            // Refine with Levenberg-Marquardt on inliers only
            std::vector<cv::Point3f> inlierObj;
            std::vector<cv::Point2f> inlierImg;
            for (int idx : inliers)
            {
                inlierObj.push_back(objectPoints[idx]);
                inlierImg.push_back(imagePoints[idx]);
            }
            cv::solvePnPRefineLM(inlierObj, inlierImg, camMatrix, distCoeffs, rvec, tvec);

            // Calculate mean reprojection error on all inliers
            std::vector<cv::Point2f> projected;
            cv::projectPoints(inlierObj, rvec, tvec, camMatrix, distCoeffs, projected);
            float totalErr = 0.0f;
            for (size_t i = 0; i < projected.size(); i++)
                totalErr += (float)cv::norm(projected[i] - inlierImg[i]);
            float meanReproj = totalErr / (float)projected.size();

            outputArr[0] = (float)tvec.at<double>(0);
            outputArr[1] = (float)tvec.at<double>(1);
            outputArr[2] = (float)tvec.at<double>(2);
            outputArr[3] = (float)rvec.at<double>(0);
            outputArr[4] = (float)rvec.at<double>(1);
            outputArr[5] = (float)rvec.at<double>(2);
            outputArr[6] = 1.0f;
            outputArr[7] = (float)inliers.size();
            outputArr[8] = meanReproj;
        }
        else
        {
            // Failure
            outputArr[6] = 0.0f;
            outputArr[7] = 0.0f;
            outputArr[8] = 999.0f;
        }
    }
}