import fs from "fs";
import path from "path";
import axios from "axios";
import { performance } from "perf_hooks";

// Function to read files in a directory and find lines with concatenated strings
export const findConcatenatedStringsInDir = async (dir) => {
  const totalStartTime = performance.now();

  try {
    const files = fs.readdirSync(dir);

    for (const file of files) {
      const fileStartTime = performance.now(); // Start time for the file
      const filePath = path.join(dir, file);

      // Check if the current file is a directory
      if (fs.lstatSync(filePath).isDirectory()) {
        // Recursively process the directory
        await findConcatenatedStringsInDir(filePath);
        continue;
      }

      // Only process C# files
      if (path.extname(file) !== '.cs') {
        continue;
      }

      const content = fs.readFileSync(filePath, "utf-8");
      const lines = content.split("\n");

      // Regex to match concatenated strings
      const regex = /".*?"\s*\+\s*.*?|.*?\s*\+\s*".*?"/;
      // Regex to detect comments
      const singleLineCommentRegex = /^\s*\/\//;
      const multiLineCommentStartRegex = /^\s*\/\*/;
      const multiLineCommentEndRegex = /\*\//;

      console.log(`>>> Processing file: ${file}`);

      let inMultiLineComment = false;

      for (const [index, line] of lines.entries()) {
        // Check for single-line or multi-line comments
        if (singleLineCommentRegex.test(line)) {
          continue;
        }

        if (multiLineCommentStartRegex.test(line)) {
          inMultiLineComment = true;
        }

        if (inMultiLineComment) {
          if (multiLineCommentEndRegex.test(line)) {
            inMultiLineComment = false;
          }
          continue;
        }

        if (regex.test(line)) {
          const leadingWhitespace = line.match(/^\s*/)[0]; // Capture leading whitespace

          const prompt = createPrompt(line.trim(), index + 1);
          console.log(`❔  Prompt for line ${index + 1}: \n${line.trim()}`);

          const startTime = performance.now();
          const response = await postFileContentToApi(prompt);
          const endTime = performance.now();
          const duration = (endTime - startTime) / 1000;
          console.log(
            `⏳ LLM response time: ${
              duration < 60
                ? `${duration.toFixed(2)} sec`
                : `${(duration / 60).toFixed(2)} min`
            }`
          );

          // Replace the line in the original file content
          if (response[index + 1 + ""]) {
            // Remove any new lines from the transformed string
            let transformedLine = response[index + 1 + ""].replace(/\n/g, " ");

            lines[index] = leadingWhitespace + transformedLine;
          }
        }
      }

      // Save the updated content back to the file
      fs.writeFileSync(filePath, lines.join("\n"), "utf-8");

      const fileEndTime = performance.now(); // End time for the file
      const fileDuration = (fileEndTime - fileStartTime) / 1000;
      console.log(
        `🕑 File processing time: ${
          fileDuration < 60
            ? `${fileDuration.toFixed(2)} sec`
            : `${(fileDuration / 60).toFixed(2)} min`
        }`
      );

      console.log("-------------------------------------------");
    }

    const totalEndTime = performance.now();
    console.log(
      `🕛 Overall running time: ${(
        (totalEndTime - totalStartTime) /
        1000 /
        60
      ).toFixed(2)} min`
    );
  } catch (error) {
    console.error("Error processing files:", error);
  }
};

// Function to create the prompt for the LLM
const createPrompt = (line, lineNumber) => {
  return `Task:
    Input:
    ${lineNumber}: ${line}
    `;
};

const handleKVSlotError = async (prompt) => {
  console.error("⚠️  KV slot error, attempting to restart the server...");
  try {
    await axios.post("http://localhost:3000/restart");
    console.log("✔️  Server restart initiated, retrying the request...");

    await new Promise((resolve) => setTimeout(resolve, 5000));

    return await findConcatenatedStringsInDir(dirPath);

  } catch (restartError) {
    console.error("❌ Error restarting the server:", restartError);
    throw restartError;
  }
};

// Function to post the file content with the prompt to the API
const postFileContentToApi = async (prompt) => {
  try {
    const response = await axios.post(
      "http://localhost:3000/process-message",
      {
        message: prompt,
      },
      {
        headers: {
          "Content-Type": "application/json",
        },
      }
    );
    const transformedData = response.data;
    console.log(`✔️   Response from LLM: \n`, transformedData);
    return transformedData;
  } catch (error) {
    if (error.response && error.response.status === 503) {
      return await handleKVSlotError(prompt);
    } else {
      console.error(
        "❌ Error calling API:",
        error.response ? error.response.data : error.message
      );
      throw error;
    }
  }
};

// Directory containing the files to be processed
const dirPath =
  "C:\\Users\\sergey.kosik\\Code\\AccountsPrep\\Common";

// Call the function to find concatenated strings in the specified directory
findConcatenatedStringsInDir(dirPath);
