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
      const filePath = path.join(dir, file);

      // Check if the current file is a directory
      if (fs.lstatSync(filePath).isDirectory()) {
        // Recursively process the directory
        await findConcatenatedStringsInDir(filePath);
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

      const linesToProcess = [];
      let inMultiLineComment = false;

      lines.forEach((line, index) => {
        // Check for single-line or multi-line comments
        if (singleLineCommentRegex.test(line)) {
          return;
        }

        if (multiLineCommentStartRegex.test(line)) {
          inMultiLineComment = true;
        }

        if (inMultiLineComment) {
          if (multiLineCommentEndRegex.test(line)) {
            inMultiLineComment = false;
          }
          return;
        }

        if (regex.test(line)) {
          const leadingWhitespace = line.match(/^\s*/)[0]; // Capture leading whitespace
          linesToProcess.push({
            lineNumber: index + 1,
            lineContent: line.trim(),
            leadingWhitespace,
          });
        }
      });

      if (linesToProcess.length > 0) {
        const promptLines = linesToProcess
          .map(({ lineNumber, lineContent }) => `${lineNumber}: ${lineContent}`)
          .join("\n");

        const prompt = createPrompt(promptLines);
        console.log(`❔  Prompt: \n${promptLines}`);

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
        console.log("-------------------------------------------");

        // Replace the lines in the original file content
        linesToProcess.forEach(({ lineNumber, leadingWhitespace }) => {
          if (response[lineNumber + ""]) {
            // Remove any new lines from the transformed string
            let transformedLine = response[lineNumber + ""].replace(/\n/g, " ");

            // Check for misplaced semicolon and fix it, then add semicolon at the end if needed
            if (transformedLine.includes('";)')) {
              transformedLine = transformedLine.replace(/(;)\s*(\)$)/g, "$2;");
            }

            lines[lineNumber - 1] = leadingWhitespace + transformedLine;
          }
        });

        // Save the updated content back to the file
        fs.writeFileSync(filePath, lines.join("\n"), "utf-8");
      }
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
const createPrompt = (lines) => {
  return `Task:
    Input:
    ${lines}
    `;
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
    console.error(
      "❌ Error calling API:",
      error.response ? error.response.data : error.message
    );
  }
};

// Directory containing the files to be processed
const dirPath = "./files";

// Call the function to find concatenated strings in the specified directory
findConcatenatedStringsInDir(dirPath);
