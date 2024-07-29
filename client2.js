import { processFilesInDir } from './processFiles.js';

// Directory containing the files to be processed
const dirPath = './files';

// Define the maximum chunk size (number of characters)
const chunkSize = 1000; // Adjust this value based on the token limit and average token size

// Call the function to process files in the specified directory
processFilesInDir(dirPath, chunkSize);
